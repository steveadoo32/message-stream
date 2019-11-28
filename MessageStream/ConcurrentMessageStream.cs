using MessageStream.IO;
using MessageStream.Message;
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
    public class ConcurrentMessageStream<T> : MessageStream<T>
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public readonly TimeSpan DefaultReaderFlushTimeout = TimeSpan.FromSeconds(1);

        private static readonly ChannelOptions DefaultReaderChannelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false
        };
        private static readonly ChannelOptions DefaultWriterChannelOptions = new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = false
        };

        private readonly TimeSpan? readerFlushTimeout;
        private readonly RequestResponseKeyResolver<T> keyResolver;
        private readonly ChannelOptions readerChannelOptions;
        private Channel<MessageReadResult<T>> readChannel;
        private Task channelReadTask;

        private readonly ChannelOptions writerChannelOptions;
        private Channel<MessageWriteRequest> writeChannel;
        private Task channelWriteTask;

        ConcurrentDictionary<string, MessageWriteRequestResult> requests;

        private long _writeChannelMessagesWritten;
        private long _writeChannelMessagesRead;
        private long _readChannelMessagesWritten;
        private long _readChannelMessagesRead;

        public long WriteChannelMessagesWritten => _writeChannelMessagesWritten;

        public long WriteChannelMessagesRead => _writeChannelMessagesRead;

        public long ReadChannelMessagesWritten => _readChannelMessagesWritten;

        public long ReadChannelMessagesRead => _readChannelMessagesRead;

        /// <summary>
        /// </summary>
        /// <param name="readerMessageBuffer">Can be UnboundedChannelOptions or BoundedChannelOptions.</param>
        /// <param name="writerMessageBuffer">Can be UnboundedChannelOptions or BoundedChannelOptions.</param>
        /// <param name="readerFlushTimeout">
        /// How long to give the readers to read the rest of the messages after the stream has closed.
        /// If you pass in null, it will wait forever. If you have no readers and pass null in you can sit forever
        /// in CloseAsync.
        /// </param>
        public ConcurrentMessageStream(
            IReader reader,
            IMessageDeserializer<T> deserializer,
            IWriter writer,
            IMessageSerializer<T> serializer,
            RequestResponseKeyResolver<T> keyResolver = null,
            PipeOptions readerPipeOptions = null,
            PipeOptions writerPipeOptions = null,
            TimeSpan? writerCloseTimeout = null,
            ChannelOptions readerChannelOptions = null,
            ChannelOptions writerChannelOptions = null,
            TimeSpan? readerFlushTimeout = null)
            : base(reader, deserializer, writer, serializer, readerPipeOptions, writerPipeOptions, writerCloseTimeout)
        {
            this.keyResolver = keyResolver;
            this.readerChannelOptions = readerChannelOptions ?? DefaultReaderChannelOptions;
            this.writerChannelOptions = writerChannelOptions ?? DefaultWriterChannelOptions;
            this.readerFlushTimeout = readerFlushTimeout;
        }

        #region Open/Close

        public override async Task OpenAsync()
        {
            _writeChannelMessagesRead = 0;
            _writeChannelMessagesWritten = 0;
            _readChannelMessagesWritten = 0;
            _readChannelMessagesRead = 0;

            await base.OpenAsync().ConfigureAwait(false);

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

            readChannel = GetChannel<MessageReadResult<T>>(readerChannelOptions);
            writeChannel = GetChannel<MessageWriteRequest>(writerChannelOptions);

            requests = new ConcurrentDictionary<string, MessageWriteRequestResult>();

            channelReadTask = Task.Run(ReadLoopAsync);
            channelWriteTask = Task.Run(WriteLoopAsync);
        }

        public async override Task CloseAsync()
        {
            // Check if we're already closed.
            if (!Open)
            {
                return;
            }

            // This will prevent new messages coming in.
            var oldWriteChannel = writeChannel;
            writeChannel = null;

            // Try to write the rest of the messages before closing.
            oldWriteChannel.Writer.Complete();
            await Task.WhenAny(
                Task.Delay(TimeSpan.FromSeconds(10)),
                oldWriteChannel.Reader.Completion
            ).ConfigureAwait(false);

            // Close the underlying stream
            await base.CloseAsync().ConfigureAwait(false);

            // There could be messages left in the buffer so we have a timeout that will try to read the rest
            if (readerFlushTimeout != null)
            {
                await Task.WhenAny(
                    Task.Delay(readerFlushTimeout.Value),
                    readChannel.Reader.Completion
                ).ConfigureAwait(false);
            }
            else
            {
                await readChannel.Reader.Completion.ConfigureAwait(false);
            }

            readChannel = null;

            await Task.WhenAll(channelReadTask, channelWriteTask).ConfigureAwait(false);

            channelReadTask = null;
            channelWriteTask = null;
        }

        #endregion

        #region Read/Write

        /// <summary>
        /// Enqueues a message that will be picked up by ReadAsync.
        /// Useful if you want to mock received messages
        /// </summary>
        public async ValueTask EnqueueMessageOnReaderAsync(T message)
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
                IsCompleted = false,
                Result = message,
                ReadResult = true
            }).ConfigureAwait(false);
        }

        public async override ValueTask<MessageReadResult<T>> ReadAsync()
        {
            var reader = readChannel?.Reader;
            if (reader == null)
            {
                return new MessageReadResult<T>
                {
                    IsCompleted = true
                };
            }

            MessageReadResult<T> result = default;
            // Check right away if we can read
            if (reader.TryRead(out result))
            {
                Interlocked.Increment(ref _readChannelMessagesRead);

                return result;
            }

            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // If its still open and try read is false, we need to wait to read again
                if (!reader.TryRead(out result))
                {
                    continue;
                }

                Interlocked.Increment(ref _readChannelMessagesRead);

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
            var writer = writeChannel?.Writer;

            if (writer == null || WriteCompleted)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var writeRequest = new MessageWriteRequest(message, null, null, flush, null);

            // Write the request
            bool writeable = false;
            if (writer.TryWrite(writeRequest))
            {
                writeable = true;
            }
            else
            {
                while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
                {
                    if (writer.TryWrite(writeRequest))
                    {
                        break;
                    }
                }
            }

            return new MessageWriteResult
            {
                IsCompleted = !writeable,
                Error = false,
                Exception = null
            };
        }

        /// <summary>
        /// Writes the message and waits until it's actually been written to the pipe. Slower than WriteAsync
        /// </summary>
        public async ValueTask<MessageWriteResult> WriteAndWaitAsync(T message, bool flush = true)
        {
            var writer = writeChannel?.Writer;
            if (writer == null || WriteCompleted)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var tcs = new TaskCompletionSource<MessageWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeRequest = new MessageWriteRequest(message, tcs, null, flush, null);

            // Write the request
            bool writeable = false;
            if (writer.TryWrite(writeRequest))
            {
                writeable = true;
            }
            else
            {
                while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
                {
                    if (writer.TryWrite(writeRequest))
                    {
                        break;
                    }
                }
            }

            if (!writeable)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            return await tcs.Task.ConfigureAwait(false);
        }


        /// <summary>
        /// Writes a message and waits for a specific message to come back. You need to register key resolvers in the constructor for this way
        /// </summary>
        public async Task<MessageWriteRequestResult<TReply>> WriteRequestAsync<TReply>(T request, TimeSpan timeout = default, bool flush = true) where TReply : T
        {
            if (keyResolver == null)
            {
                throw new Exception("KeyResolver must be set if using this method.");
            }

            string key = keyResolver.GetKey(request);
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
            var writer = writeChannel?.Writer;
            if (writer == null || WriteCompleted)
            {
                return new MessageWriteRequestResult<TReply>
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

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
            if (writer.TryWrite(writeRequest))
            {
                Interlocked.Increment(ref _writeChannelMessagesWritten);

                writeable = true;
            }
            else
            {
                while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
                {
                    if (writer.TryWrite(writeRequest))
                    {
                        Interlocked.Increment(ref _writeChannelMessagesWritten);

                        break;
                    }
                }
            }

            if (!writeable)
            {
                return new MessageWriteRequestResult<TReply>
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null,
                    Result = default
                };
            }

            MessageReadResult<T> result = await resultTcs.Task.ConfigureAwait(false);

            // Make sure we dispose our cts
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
                ReadResult = result.ReadResult
            };

            return new MessageWriteRequestResult<TReply>
            {
                IsCompleted = false,
                Error = false,
                Exception = null,
                Result = castedResult
            };
        }

        private static bool DefaultReplyMatch(T reply, Type replyType)
        {
            return reply.GetType() == replyType;
        }

        #endregion

        #region Read/Write loops

        private async Task ReadLoopAsync()
        {
            var writer = readChannel.Writer;

            // Use a linked list because 99% of the time we should be matching the requests in order,
            // so we'll end up removing the first element of the list.
            bool readOuter = true;
            try
            {
                bool currResultRequestHandled = false;
                MessageReadResult<T> result = await base.ReadAsync().ConfigureAwait(false);
                while (readOuter && await writer.WaitToWriteAsync().ConfigureAwait(false))
                {
                    while (true)
                    {
                        // Only dequeue the ones in this batch. We can end up being stuck here if there are 
                        // messages being written at a faster rate than we read
                        if (!currResultRequestHandled)
                        {
                            if (result.ReadResult)
                            {
                                var localRequests = requests;
                                var responseKey = result.Result is IResponse response ? response.GetKey() : keyResolver?.GetKey(result.Result);
                                MessageWriteRequestResult requestResult = default;
                                if (responseKey != null && (localRequests?.TryRemove(responseKey, out requestResult) ?? false))
                                {
                                    requestResult.resultTcs.TrySetResult(result);
                                }
                            }
                        }
                        
                        // Don't process this request logic again if we have to wait to write.
                        currResultRequestHandled = true;

                        // Try to write, if fails, we just wait to write this result again.
                        if (!writer.TryWrite(result))
                        {
                            break;
                        }

                        Interlocked.Increment(ref _readChannelMessagesWritten);

                        if (result.IsCompleted)
                        {
                            readOuter = false;
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
                Logger.Error(ex, "Error in read loop.");
            }

            writer.Complete();
        }

        private async Task WriteLoopAsync()
        {
            var reader = writeChannel?.Reader;
            // Means stream is closed.
            if (reader == null)
            {
                return;
            }

            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var writeRequest))
                    {
                        Interlocked.Increment(ref _writeChannelMessagesRead);

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

                            // If the stream is closed we can quickly complete the request here. we dont care about the message still being in the request queue because that task will complete soon as well.
                            if ((writeResult.Error || writeResult.IsCompleted) && writeRequest.resultTcs != null)
                            {
                                request.resultTcs.TrySetResult(new MessageReadResult<T>
                                {
                                    Error = writeResult.Error,
                                    Exception = writeResult.Exception,
                                    IsCompleted = writeResult.IsCompleted,
                                    ReadResult = false,
                                    Result = default
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Error writing message in concurrent message stream.");
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in concurrent message stream write loop.");
                // This happens if the underlying stream is closed and we have a message in flight.
                // We can just ignore it.
            }

            // process left over requests.
            while (reader.TryRead(out var writeRequest))
            {
                MessageWriteRequestResult request = default;
                if (writeRequest.resultTcs != null)
                {
                    request = new MessageWriteRequestResult(writeRequest.resultTcs);
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

                var writeResult = await base.WriteAsync(writeRequest.message, writeRequest.flush).ConfigureAwait(false);
                if (writeRequest.writeTcs != null)
                {
                    writeRequest.writeTcs.TrySetResult(writeResult);
                }
            }

            // Cancel pending requests
            foreach (var requestResult in requests.Values)
            {
                requestResult.resultTcs.TrySetResult(new MessageReadResult<T>
                {
                    Error = true,
                    Exception = new TaskCanceledException("Request timed out"),
                    IsCompleted = false,
                    Result = default,
                    ReadResult = false
                });
            }

            requests = null;
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
                TaskCompletionSource<MessageWriteResult> tcs,
                TaskCompletionSource<MessageReadResult<T>> resultTcs,
                bool flush,
                string requestKey) : this()
            {
                this.message = message;
                this.writeTcs = tcs;
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
