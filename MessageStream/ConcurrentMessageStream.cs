using MessageStream.Message;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessageStream
{

    public delegate ValueTask UnexpectedCloseDelegateAsync(Exception ex);

    public class ConcurrentMessageStream<T> : MessageStream<T>
    {

        private readonly UnexpectedCloseDelegateAsync unexpectedCloseDelegateAsync;
        protected readonly ConcurrentMessageStreamOptions concurrentOptions;

        private readonly RequestResponseKeyResolver<T> rpcKeyResolver;
        private readonly ConcurrentDictionary<string, MessageWriteRequestResult> requests = new ConcurrentDictionary<string, MessageWriteRequestResult>();

        // This prevents new messages from getting written.
        private bool closing;
        private int closeCounter = 0;

        // Our channels
        private Channel<MessageReadResult<T>> readChannel;
        private Channel<MessageWriteRequest> writeChannel;

        private Task readChannelTask;
        private Task readChannelCleanupTask;
        private Task writeChannelTask;
        private Task writeChannelCleanupTask;

        public ConcurrentMessageStreamChannelStats ReadChannelStats { get; } = new ConcurrentMessageStreamChannelStats();

        public ConcurrentMessageStreamChannelStats WriteChannelStats { get; } = new ConcurrentMessageStreamChannelStats();

        public ConcurrentMessageStream(
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
            IDuplexMessageStream duplexMessageStream,
            UnexpectedCloseDelegateAsync unexpectedCloseDelegate,
            ConcurrentMessageStreamOptions options = null,
            RequestResponseKeyResolver<T> rpcKeyResolver = null)
            : base(deserializer, serializer, duplexMessageStream, options ?? new ConcurrentMessageStreamOptions())
        {
            this.unexpectedCloseDelegateAsync = unexpectedCloseDelegate;
            this.concurrentOptions = this.options as ConcurrentMessageStreamOptions;
            this.rpcKeyResolver = rpcKeyResolver;
        }

        public override async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            if (Open)
            {
                throw new MessageStreamOpenException("MessageStream already open");
            }

            requests.Clear();
            closeCounter = 0;
            closing = false;
            ReadChannelStats.Reset();
            WriteChannelStats.Reset();

            // Open the underlying stream first
            try
            {
                await base.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }

            // Creates our read/write channels
            Channel<TInner> GetChannel<TInner>(ChannelOptions options)
            {
                if (options is UnboundedChannelOptions)
                {
                    return Channel.CreateUnbounded<TInner>((UnboundedChannelOptions)options);
                }
                else
                {
                    return Channel.CreateBounded<TInner>((BoundedChannelOptions)options);
                }
            }

            readChannel = GetChannel<MessageReadResult<T>>(concurrentOptions.ReaderChannelOptions);
            writeChannel = GetChannel<MessageWriteRequest>(concurrentOptions.WriterChannelOptions);

            readChannelTask = concurrentOptions.ChannelTaskFactory.StartNew(ReadLoopAsync, concurrentOptions.ReadChannelTaskOptions).Unwrap();
            writeChannelTask = concurrentOptions.ChannelTaskFactory.StartNew(WriteLoopAsync, concurrentOptions.WriteChannelTaskOptions).Unwrap();

            if (readChannelTask.IsCompleted && !readChannelTask.IsCompletedSuccessfully)
            {
                await CleanupOpenAsync().ConfigureAwait(false);
                throw readChannelTask.Exception;
            }

            if (writeChannelTask.IsCompleted && !writeChannelTask.IsCompletedSuccessfully)
            {
                await CleanupOpenAsync().ConfigureAwait(false);
                throw writeChannelTask.Exception;
            }

            readChannelCleanupTask = readChannel.Reader.Completion.ContinueWith((result) => ReadChannelCompleteAsync(result), TaskContinuationOptions.RunContinuationsAsynchronously);
            writeChannelCleanupTask = writeChannel.Reader.Completion.ContinueWith((result) => WriteChannelCompleteAsync(result), TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        public override async Task CloseAsync()
        {
            if (!Open)
            {
                throw new MessageStreamOpenException("MessageStream is not open.");
            }

            Logger?.LogInformation("Closing concurrent message stream.");

            // Prevent new writes and prevents us from closing the stream a second time if the completion cleanup tasks get run
            closing = true;

            // Close the base stream. this will complete our channels.
            try
            {
                await base.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error closing underlying message stream.");

                // Try to force close channels.
                CompleteChannels();
            }

            Logger?.LogTrace("Closed underlying stream.");

            // Complete our channels
            var channelsCompletionTask = Task.WhenAll(readChannel.Reader.Completion.ContinueWith(r => r.IsCompletedSuccessfully), writeChannel.Reader.Completion.ContinueWith(r => r.IsCompletedSuccessfully));
            if (concurrentOptions.ChannelCloseTimeout.HasValue)
            {
                await Task.WhenAny(Task.Delay(concurrentOptions.ChannelCloseTimeout.Value), channelsCompletionTask).ConfigureAwait(false);
                // Force the channels to complete.
                if (!channelsCompletionTask.IsCompleted)
                {
                    Logger?.LogWarning($"Channels completion tasks were not completed within {concurrentOptions.ChannelCloseTimeout}");
                    CompleteChannels();
                }
            }
            else
            {
                await Task.WhenAny(channelsCompletionTask).ConfigureAwait(false);
            }

            Logger?.LogTrace("Completed channels.");

            // Wait for channel clean up tasks to finish
            var channelsCleanupTask = Task.WhenAll(readChannelCleanupTask.ContinueWith(r => r.IsCompletedSuccessfully), writeChannelCleanupTask.ContinueWith(r => r.IsCompletedSuccessfully));
            if (concurrentOptions.ChannelCloseTimeout.HasValue)
            {
                await Task.WhenAny(Task.Delay(concurrentOptions.ChannelCloseTimeout.Value), channelsCleanupTask).ConfigureAwait(false);
                if (!channelsCleanupTask.IsCompleted)
                {
                    Logger?.LogWarning($"Channels clean up tasks were not completed within {concurrentOptions.ChannelCloseTimeout}");
                }
            }
            else
            {
                await Task.WhenAny(channelsCompletionTask).ConfigureAwait(false);
            }

            Logger?.LogTrace("Completed channel cleanup.");

            Cleanup();

            Logger?.LogInformation("Closed concurrent message stream.");
        }

        private async Task ReadChannelCompleteAsync(Task completionTask)
        {
            if (!closing)
            {
                // This indicates we completed before we requested to close, so we should close ourselves.
                _ = HandleUnexpectedCloseAsync(completionTask);
            }
            // not much else to do here.
        }

        private async Task WriteChannelCompleteAsync(Task completionTask)
        {
            if (!closing)
            {
                // This indicates we completed before we requested to close, so we should close ourselves.
                _ = HandleUnexpectedCloseAsync(completionTask);
            }
            // not much else to do here.
        }

        private Task HandleUnexpectedCloseAsync(Task completionTask)
        {
            int counter = Interlocked.Increment(ref closeCounter);

            // Something already started to close.
            if (counter != 1)
            {
                return Task.CompletedTask;
            }

            Logger?.LogError(completionTask.Exception, "MessageStream closed unexpectedly");

            return Task.Run(CloseAsync).ContinueWith(result => NotifyClosedAsync(completionTask.Exception), TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        private async Task NotifyClosedAsync(Exception ex = null)
        {
            // todo try to get exception from underlying stream if we can
            await unexpectedCloseDelegateAsync(ex).ConfigureAwait(false);
        }

        private async ValueTask CleanupOpenAsync()
        {
            await base.CloseAsync().ConfigureAwait(false);
            Cleanup();
        }

        private void CompleteChannels()
        {
            readChannel?.Writer.TryComplete();
            writeChannel?.Writer.TryComplete();
        }

        private void Cleanup()
        {
            // Stop channels
            CompleteChannels();

            // Reset stats
            ReadChannelStats.Reset();
            WriteChannelStats.Reset();

            // Complete any left over requests.
            foreach (var request in requests.Values)
            {
                request.resultTcs.TrySetResult(new MessageReadResult<T>
                {
                    Error = true,
                    Exception = new TaskCanceledException("Request timed out"),
                    IsCompleted = true,
                    Result = default,
                    ReadResult = false
                });
            }

            requests.Clear();
        }

        #region Read/Write

        /// <summary>
        /// Enqueues a message that will be picked up by ReadAsync.
        /// Useful if you want to mock received messages
        /// </summary>
        public async ValueTask EnqueueMessageOnReaderAsync(T message, bool complete = false)
        {
            var writer = readChannel?.Writer;
            if (writer == null)
            {
                return;
            }

            await writer.WriteAsync(new MessageReadResult<T>
            {
                Error = false,
                Exception = null,
                IsCompleted = complete,
                Result = message,
                ReadResult = true
            }).ConfigureAwait(false);
        }

        public async override ValueTask<MessageReadResult<T>> ReadAsync()
        {
            var reader = readChannel?.Reader;
            // we only check if we have a reader here, because this could get called before open
            // we dont use open because there could be data left over after the underlying stream is closed.
            if (reader == null)
            {
                // The reader is closed. There might still be a result 
                return new MessageReadResult<T>
                {
                    IsCompleted = true,
                    ReadResult = false
                };
            }

            MessageReadResult<T> result = default;
            // Check right away if we can read
            if (reader.TryRead(out result))
            {
                ReadChannelStats.IncReadChannelMessagesRead();

                return result;
            }

            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // If its still open and try read is false, we need to wait to read again
                if (!reader.TryRead(out result))
                {
                    continue;
                }

                ReadChannelStats.IncReadChannelMessagesRead();

                return result;
            }

            // The reader is closed. There might still be a result 
            return new MessageReadResult<T>
            {
                IsCompleted = true,
                ReadResult = false
            };
        }

        /// <summary>
        /// NOTE: All writes will be reported as success because messages are dropped
        /// into a queue that are written later. If you would like to wait for an actual result on the write,
        /// use WriteAndWaitAsync
        /// </summary>
        public async override ValueTask<MessageWriteResult> WriteAsync(T message, bool flush = true)
        {
            if (!Open || closing)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var writer = writeChannel?.Writer;
            var writeRequest = new MessageWriteRequest(message, null, null, flush, null);

            // Write the request
            bool writeable = false;
            Exception outerEx = null;
            try
            {
                if (writer.TryWrite(writeRequest))
                {
                    WriteChannelStats.IncReadChannelMessagesSubmitted();

                    writeable = true;
                }
                else
                {
                    while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
                    {
                        if (writer.TryWrite(writeRequest))
                        {
                            WriteChannelStats.IncReadChannelMessagesSubmitted();

                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                outerEx = ex;
                writeable = false;
            }

            return new MessageWriteResult
            {
                IsCompleted = !writeable,
                Error = outerEx != null,
                Exception = outerEx
            };
        }

        /// <summary>
        /// Writes the message and waits until it's actually been written to the pipe. Slower than WriteAsync
        /// </summary>
        public async ValueTask<MessageWriteResult> ConfirmWriteAsync(T message, bool flush = true)
        {
            if (!Open || closing)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var writer = writeChannel?.Writer;
            var tcs = new TaskCompletionSource<MessageWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeRequest = new MessageWriteRequest(message, tcs, null, flush, null);

            // Write the request
            bool writeable = false;
            Exception outerEx = null;
            try
            {
                if (writer.TryWrite(writeRequest))
                {
                    WriteChannelStats.IncReadChannelMessagesSubmitted();

                    writeable = true;
                }
                else
                {
                    while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
                    {
                        if (writer.TryWrite(writeRequest))
                        {
                            WriteChannelStats.IncReadChannelMessagesSubmitted();

                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                outerEx = ex;
                writeable = false;
            }

            if (!writeable)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = outerEx != null,
                    Exception = outerEx
                };
            }

            return await tcs.Task.ConfigureAwait(false);
        }


        /// <summary>
        /// Writes a message and waits for a specific message to come back. You need to register key resolvers in the constructor for this way
        /// </summary>
        public async Task<MessageWriteRequestResult<TReply>> WriteRequestAsync<TReply>(T request, TimeSpan timeout = default, bool flush = true) where TReply : T
        {
            if (rpcKeyResolver == null)
            {
                throw new Exception("KeyResolver must be set if using this method.");
            }

            string key = rpcKeyResolver.GetKey(request);
            if (key == null)
            {
                throw new ArgumentException($"Cannot find key for request {request}.");
            }

            return await WriteRequestInternalAsync<TReply>(request, timeout, flush, key).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a message and waits for a specific message to come back.
        /// </summary>
        public async Task<MessageWriteRequestResult<TReply>> WriteRequestAsync<TRequest, TReply>(TRequest request, TimeSpan timeout = default, bool flush = true) where TRequest : T, IRequest where TReply : T, IResponse
        {
            return await WriteRequestInternalAsync<TReply>(request, timeout, flush, request.GetKey()).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a message and waits for a specific message to come back.
        /// </summary>
        private async Task<MessageWriteRequestResult<TReply>> WriteRequestInternalAsync<TReply>(T request, TimeSpan timeout, bool flush, string requestKey) where TReply : T
        {
            if (!Open || closing)
            {
                return new MessageWriteRequestResult<TReply>
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var writer = writeChannel?.Writer;
            TaskCompletionSource<MessageReadResult<T>> resultTcs = new TaskCompletionSource<MessageReadResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeRequest = new MessageWriteRequest(request, null, resultTcs, flush, requestKey);

            CancellationToken cancellationToken = default;
            CancellationTokenRegistration cancellationTokenTokenRegistration = default;
            if (timeout.TotalMilliseconds > 0)
            {
                cancellationToken = CoalescedTokens.FromTimeout(timeout);
                cancellationTokenTokenRegistration = cancellationToken.Register(state =>
                {
                    var tcs = (TaskCompletionSource<MessageReadResult<T>>)state;
                    // todo move this somewhere else.
                    requests?.TryRemove(requestKey, out var messageWriteRequestResult);
                    tcs.TrySetResult(new MessageReadResult<T>
                    {
                        Error = true,
                        Exception = new TaskCanceledException("Request timed out"),
                        IsCompleted = false,
                        Result = default,
                        ReadResult = false
                    });
                }, resultTcs);
            }

            // Write the message and await the read task.
            bool writeable = false;
            Exception outerEx = null;
            try
            {
                if (writer.TryWrite(writeRequest))
                {
                    WriteChannelStats.IncReadChannelMessagesSubmitted();

                    writeable = true;
                }
                else
                {
                    while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
                    {
                        if (writer.TryWrite(writeRequest))
                        {
                            WriteChannelStats.IncReadChannelMessagesSubmitted();

                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                outerEx = ex;
                writeable = false;
            }

            if (!writeable)
            {
                return new MessageWriteRequestResult<TReply>
                {
                    IsCompleted = true,
                    Error = outerEx != null,
                    Exception = outerEx,
                    Result = default
                };
            }

            // TODO use response task factory
            MessageReadResult<T> result = await resultTcs.Task.ConfigureAwait(false);

            // Make sure we dispose our cts registration
            if (timeout.TotalMilliseconds > 0)
            {
                cancellationTokenTokenRegistration.Dispose();
            }

            MessageReadResult<TReply> castedResult = new MessageReadResult<TReply>
            {
                Error = result.Error,
                Exception = result.Exception,
                IsCompleted = result.IsCompleted,
                Result = (TReply)result.Result,
                ReadResult = result.ReadResult,
                ParsedTimeUtc = result.ParsedTimeUtc,
                ReceivedTimeUtc = result.ReceivedTimeUtc
            };

            return new MessageWriteRequestResult<TReply>
            {
                IsCompleted = false,
                Error = false,
                Exception = null,
                Result = castedResult
            };
        }

        #endregion

        #region Read/Write loops

        private async Task ReadLoopAsync()
        {
            var writer = readChannel.Writer;

            Exception outerEx = null;
            bool readOuter = true;
            try
            {
                bool currResultRequestHandled = false;
                MessageReadResult<T> result = await base.ReadAsync().ConfigureAwait(false);
                while (readOuter && await writer.WaitToWriteAsync().ConfigureAwait(false))
                {
                    while (true)
                    {
                        if (!currResultRequestHandled && result.ReadResult && result.Result != null) // how?
                        {
                            var responseKey = result.Result is IResponse response ? response.GetKey() : rpcKeyResolver?.GetKey(result.Result);
                            MessageWriteRequestResult requestResult = default;
                            if (responseKey != null && requests.TryRemove(responseKey, out requestResult))
                            {
                                requestResult.resultTcs.TrySetResult(result);
                            }
                        }

                        // Don't process this request logic again if we have to wait to write.
                        currResultRequestHandled = true;

                        // Try to write, if fails, we just wait to write this result again.
                        if (result.ReadResult && !writer.TryWrite(result))
                        {
                            break;
                        }

                        ReadChannelStats.IncReadChannelMessagesSubmitted();

                        if (result.IsCompleted)
                        {
                            readOuter = false;
                            break;
                        }

                        if (result.Error)
                        {
                            readOuter = false;
                            outerEx = result.Exception;
                            break;
                        }

                        // Mark the next message to process request logic.
                        currResultRequestHandled = false;

                        // Read the next result
                        result = await base.ReadAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                // Complete error
                outerEx = ex;
                Logger?.LogError(ex, "Error in ConcurrentMessageStream read loop.");
            }

            writer.TryComplete(outerEx);
            // we want to close the write channel too.
            writeChannel.Writer.TryComplete(outerEx);

            Logger?.LogInformation("Concurrent message stream read channel loop completed.");
        }

        private async Task WriteLoopAsync()
        {
            var reader = writeChannel.Reader;

            Exception outerEx = null;
            bool readOuter = true;
            try
            {
                int readCounter = 0;
                bool flushed = false;
                while (readOuter && await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    readCounter = 0;
                    flushed = false;

                    while (reader.TryRead(out var writeRequest))
                    {
                        readCounter++;

                        WriteChannelStats.IncReadChannelMessagesRead();

                        MessageWriteRequestResult request = default;
                        if (writeRequest.resultTcs != null)
                        {
                            request = new MessageWriteRequestResult(writeRequest.resultTcs);
                            // This isn't a great design.
                            if (!requests.TryAdd(writeRequest.requestKey, request))
                            {
                                writeRequest.resultTcs.TrySetResult(new MessageReadResult<T>
                                {
                                    Error = false,
                                    ReadResult = false,
                                    IsCompleted = false,
                                    Exception = new DuplicateRequestException($"Duplicate request id {writeRequest.requestKey}"),
                                    ReceivedTimeUtc = DateTime.UtcNow,
                                    ParsedTimeUtc = DateTime.UtcNow
                                });
                                continue;
                            }
                        }

                        try
                        {
                            var writeResult = await base.WriteAsync(writeRequest.message, writeRequest.flush).ConfigureAwait(false);
                            if (writeRequest.writeTcs != null)
                            {
                                writeRequest.writeTcs.TrySetResult(writeResult);
                            }

                            if (writeResult.Error && writeRequest.resultTcs != null)
                            {
                                requests.TryRemove(writeRequest.requestKey, out var _);
                                writeRequest.resultTcs.TrySetResult(new MessageReadResult<T>
                                {
                                    Error = writeResult.Error,
                                    ReadResult = false,
                                    IsCompleted = false,
                                    Exception = writeResult.Exception,
                                    ReceivedTimeUtc = DateTime.UtcNow,
                                    ParsedTimeUtc = DateTime.UtcNow
                                });
                            }

                            // If the stream is closed we can quickly complete the request here. we dont care about the message still being in the request queue because that task will complete soon as well.
                            if (writeResult.IsCompleted)
                            {
                                readOuter = false;
                                break;
                            }

                            // If the stream is closed we can quickly complete the request here. we dont care about the message still being in the request queue because that task will complete soon as well.
                            if (writeResult.Error)
                            {
                                readOuter = false;
                                outerEx = writeResult.Exception;
                                break;
                            }

                            if (!writeRequest.flush)
                            {
                                if (!concurrentOptions.WriteMessageCountFlushThreshold.HasValue || readCounter >= concurrentOptions.WriteMessageCountFlushThreshold)
                                {
                                    await base.FlushAsync().ConfigureAwait(false);
                                    readCounter = 0;
                                    flushed = true;
                                }
                                else
                                {
                                    flushed = false;
                                }
                            }
                            else
                            {
                                flushed = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            readOuter = false;
                            Logger?.LogError(ex, "Error writing message in concurrent message stream.");
                            outerEx = ex;
                            break;
                        }
                    }

                    if (readOuter && readCounter > 0 && !flushed)
                    {
                        await base.FlushAsync().ConfigureAwait(false);
                        readCounter = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // we dont want anymore messages at this point
                Logger?.LogError(ex, "Error in concurrent message stream write loop.");
                outerEx = ex;
            }

            // Try to set the rest of the requests to timed out in the channel
            while (reader.TryRead(out var writeRequest))
            {
                requests.TryRemove(writeRequest.requestKey, out var _);
                writeRequest.resultTcs.TrySetResult(new MessageReadResult<T>
                {
                    Error = true,
                    Exception = new TaskCanceledException("Request timed out"),
                    IsCompleted = true,
                    Result = default,
                    ReadResult = false
                });
            }

            writeChannel.Writer.TryComplete(outerEx);
            // we want to close the read channel too.
            readChannel.Writer.TryComplete(outerEx);

            Logger?.LogInformation("Concurrent message stream write channel loop completed.");
        }


        #endregion

        internal struct MessageWriteRequest
        {

            public T message;
            public TaskCompletionSource<MessageWriteResult> writeTcs;
            public TaskCompletionSource<MessageReadResult<T>> resultTcs;
            public bool flush;
            public string requestKey;

            public MessageWriteRequest(T message,
                TaskCompletionSource<MessageWriteResult> writeTcs,
                TaskCompletionSource<MessageReadResult<T>> resultTcs,
                bool flush,
                string requestKey) : this()
            {
                this.message = message;
                this.writeTcs = writeTcs;
                this.resultTcs = resultTcs;
                this.flush = flush;
                this.requestKey = requestKey;
            }

        }

        internal struct MessageWriteRequestResult
        {

            public TaskCompletionSource<MessageReadResult<T>> resultTcs;

            public MessageWriteRequestResult(
                TaskCompletionSource<MessageReadResult<T>> resultTcs
            ) : this()
            {
                this.resultTcs = resultTcs;
            }

        }


    }
}
