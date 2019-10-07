﻿using MessageStream.IO;
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
        private readonly ChannelOptions readerChannelOptions;
        private Channel<MessageReadResult<T>> readChannel;
        private Task channelReadTask;

        private readonly ChannelOptions writerChannelOptions;
        private Channel<MessageWriteRequest> writeChannel;
        private Task channelWriteTask;

        private ConcurrentQueue<MessageWriteRequestResult> requestQueue;

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
            PipeOptions readerPipeOptions = null,
            PipeOptions writerPipeOptions = null,
            TimeSpan? writerCloseTimeout = null,
            ChannelOptions readerChannelOptions = null,
            ChannelOptions writerChannelOptions = null,
            TimeSpan? readerFlushTimeout = null)
            : base(reader, deserializer, writer, serializer, readerPipeOptions, writerPipeOptions, writerCloseTimeout)
        {
            this.readerChannelOptions = readerChannelOptions ?? DefaultReaderChannelOptions;
            this.writerChannelOptions = writerChannelOptions ?? DefaultWriterChannelOptions;
            this.readerFlushTimeout = readerFlushTimeout;
        }

        #region Open/Close

        public override async Task OpenAsync()
        {
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

            requestQueue = new ConcurrentQueue<MessageWriteRequestResult>();

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

            // Try to write the rest of the messages before closing.
            writeChannel.Writer.Complete();
            await writeChannel.Reader.Completion.ConfigureAwait(false);

            writeChannel = null;

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

            requestQueue = null;
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
                return result;
            }

            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // If its still open and try read is false, we need to wait to read again
                if (!reader.TryRead(out result))
                {
                    continue;
                }

                return result;
            }

            // The reader is closed. There might still be a result 
            return new MessageReadResult<T>
            {
                IsCompleted = true
            };
        }

        /// <summary>
        /// NOTE: All writes will be reported as success because messages are dropped
        /// into a queue that are written later. If you would like to wait for an actual result on the write,
        /// use WriteAndWaitAsync
        /// </summary>
        public async override ValueTask<MessageWriteResult> WriteAsync(T message, bool flush = true)
        {
            var writer = writeChannel.Writer;

            if (writer == null)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var writeRequest = new MessageWriteRequest(message, null, null, null, flush);

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
            if (writer == null)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var tcs = new TaskCompletionSource<MessageWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeRequest = new MessageWriteRequest(message, tcs, null, null, flush);

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
        /// Writes a message and waits for a specific message to come back.
        /// </summary>
        public async ValueTask<MessageWriteRequestResult<TReply>> WriteRequestAsync<TReply>(T message, Func<T, bool> matchFunc = null, int timeoutMilliseconds = -1, bool flush = true) where TReply : T
        {
            // TODO it will complicate the code, but we can save allocations on this delegate if we 
            // push this default match behavior into the read loop. We can just attach the type of TReply
            // to MessageWriteRequest.
            matchFunc = matchFunc ?? (reply => DefaultReplyMatch(reply, typeof(TReply)));

            var writer = writeChannel?.Writer;
            if (writer == null)
            {
                return new MessageWriteRequestResult<TReply>
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null
                };
            }

            var resultTcs = new TaskCompletionSource<MessageReadResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeRequest = new MessageWriteRequest(message, null, resultTcs, matchFunc, flush);

            // Write the message and await the read task.
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
                return new MessageWriteRequestResult<TReply>
                {
                    IsCompleted = true,
                    Error = false,
                    Exception = null,
                    ReadResult = default
                };
            }

            if (timeoutMilliseconds > -1)
            {
                var cts = new CancellationTokenSource(timeoutMilliseconds);
                cts.Token.Register(state =>
                {
                    var tcs = (TaskCompletionSource<MessageReadResult<T>>)state;
                    tcs.TrySetCanceled(cts.Token);
                }, resultTcs);
            }

            MessageReadResult<T> result = await resultTcs.Task.ConfigureAwait(false);

            // This is dangerous, user has to be careful they're reply check is the right type.
            MessageReadResult<TReply> castedResult = new MessageReadResult<TReply>
            {
                Error = result.Error,
                Exception = result.Exception,
                IsCompleted = result.IsCompleted,
                Result = (TReply)result.Result
            };

            return new MessageWriteRequestResult<TReply>
            {
                IsCompleted = true,
                Error = false,
                Exception = null,
                ReadResult = castedResult
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
            var requests = new LinkedList<MessageWriteRequestResult>();

            int requestQueueDequeueCount = 0;
            int requestQueueDequeueMax = 0;

            bool readOuter = true;
            MessageReadResult<T> result = await base.ReadAsync().ConfigureAwait(false);
            while (readOuter && await writer.WaitToWriteAsync().ConfigureAwait(false))
            {
                while (true)
                {
                    // Try to write, if fails, we just wait to write this result again.
                    if (!writer.TryWrite(result))
                    {
                        break;
                    }

                    if (!requestQueue.IsEmpty)
                    {
                        // Only dequeue the ones in this batch. We can end up being stuck here if there are 
                        // messages being written at a faster rate than we read
                        requestQueueDequeueCount = 0;
                        requestQueueDequeueMax = requestQueue.Count;

                        while (requestQueue.TryDequeue(out var request))
                        {
                            requests.AddLast(request);

                            if (requestQueueDequeueCount >= requestQueueDequeueMax)
                            {
                                break;
                            }
                        }
                    }

                    if (result.ReadResult && requests.Count > 0)
                    {
                        // Loop through the linked list and complete any requests that we match against.
                        var currentNode = requests.First;
                        while (currentNode != null)
                        {
                            // If we match we need to set the result and remove the request
                            if (currentNode.Value.resultMatchFunc(result.Result))
                            {
                                currentNode.Value.resultTcs.TrySetResult(result);

                                requests.Remove(currentNode);
                            }

                            // If the timeout was hit on the tcs, then remove it so we don't keep processing it.
                            if (currentNode.Value.resultTcs.Task.IsCanceled)
                            {
                                requests.Remove(currentNode);
                            }

                            currentNode = currentNode.Next;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        readOuter = false;
                        break;
                    }

                    // Read the next result
                    result = await base.ReadAsync().ConfigureAwait(false);
                }
            }

            // Clear out the rest of the waiting requests.
            while (requestQueue.TryDequeue(out var request))
            {
                requests.AddLast(request);
            }

            foreach (var request in requests)
            {
                request.resultTcs.TrySetCanceled();
            }

            requests.Clear();
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

                        if (writeRequest.resultTcs != null)
                        {
                            requestQueue.Enqueue(new MessageWriteRequestResult(writeRequest.resultTcs, writeRequest.resultMatchFunc));
                        }

                        var writeResult = await base.WriteAsync(writeRequest.message, writeRequest.flush).ConfigureAwait(false);

                        if (writeRequest.writeTcs != null)
                        {
                            writeRequest.writeTcs.TrySetResult(writeResult);
                        }

                        // Check if the stream is closed.
                        if (writeResult.IsCompleted)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // This happens if the underlying stream is closed and we have a message in flight.
                // We can just ignore it.
            }

            // process left over requests.
            while (reader.TryRead(out var writeRequest))
            {
                var writeResult = await base.WriteAsync(writeRequest.message, writeRequest.flush).ConfigureAwait(false);
                if (writeRequest.writeTcs != null)
                {
                    writeRequest.writeTcs.TrySetResult(writeResult);
                }
            }
        }


        #endregion

        internal struct MessageWriteRequest
        {

            public T message;
            public TaskCompletionSource<MessageWriteResult> writeTcs;
            public TaskCompletionSource<MessageReadResult<T>> resultTcs;
            public Func<T, bool> resultMatchFunc;
            public bool flush;

            public MessageWriteRequest(T message,
                TaskCompletionSource<MessageWriteResult> tcs,
                TaskCompletionSource<MessageReadResult<T>> resultTcs,
                Func<T, bool> resultMatchFunc,
                bool flush) : this()
            {
                this.message = message;
                this.writeTcs = tcs;
                this.resultTcs = resultTcs;
                this.resultMatchFunc = resultMatchFunc;
                this.flush = flush;
            }

        }

        internal struct MessageWriteRequestResult
        {

            public TaskCompletionSource<MessageReadResult<T>> resultTcs;
            public Func<T, bool> resultMatchFunc;

            public MessageWriteRequestResult(
                TaskCompletionSource<MessageReadResult<T>> resultTcs,
                Func<T, bool> resultMatchFunc) : this()
            {
                this.resultTcs = resultTcs;
                this.resultMatchFunc = resultMatchFunc;
            }

        }

    }
}
