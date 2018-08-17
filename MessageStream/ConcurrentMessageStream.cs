using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessageStream.IO;
using MessageStream.Message;

namespace MessageStream
{
    public class ConcurrentMessageStream<T> : MessageStream<T>
    {

        public readonly TimeSpan DefaultReaderFlushTimeout = TimeSpan.FromSeconds(1);

        private const int DefaultBufferSize = 4096;

        private static readonly ChannelOptions DefaultChannelOptions = new BoundedChannelOptions(DefaultBufferSize);

        private readonly TimeSpan? readerFlushTimeout;
        private readonly ChannelOptions readerChannelOptions;
        private Channel<MessageReadResult<T>> readChannel;
        private Task channelReadTask;

        private readonly ChannelOptions writerChannelOptions;
        private Channel<MessageWriteRequest> writeChannel;
        private Task channelWriteTask;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="readerMessageBuffer">Pass in null to create an unbounded channel</param>
        /// <param name="writerMessageBuffer">Pass in null to create an unbounded channel</param>
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
            this.readerChannelOptions = readerChannelOptions ?? DefaultChannelOptions;
            this.writerChannelOptions = writerChannelOptions ?? DefaultChannelOptions;
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

            channelReadTask = Task.Run(ReadLoopAsync);
            channelWriteTask = Task.Run(WriteLoopAsync);
        }

        public async override Task CloseAsync()
        {
            // Try to write the rest of the messages before closing.
            writeChannel.Writer.Complete();
            await writeChannel.Reader.Completion.ConfigureAwait(false);

            writeChannel = null;

            // Close the underlying stream
            await base.CloseAsync().ConfigureAwait(false);

            // Try to read any leftover messages.

            // If there are no readers on the stream this can end up sitting here forever,
            // So give readers 100ms to read the last messages before closing.
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

        public async override ValueTask<MessageReadResult<T>> ReadAsync()
        {
            var reader = readChannel.Reader;

            var finished = await reader.WaitToReadAsync().ConfigureAwait(false);
            if (!finished || !reader.TryRead(out var result))
            {
                return new MessageReadResult<T>
                {
                    IsCompleted = true
                };
            }

            return result;
        }

        /// <summary>
        /// NOTE: All writes will be reported as success because messages are dropped
        /// into a queue that are written later. If you would like to wait for an actual result on the write,
        /// use WriteAndWaitAsync
        /// </summary>
        public async override ValueTask<MessageWriteResult> WriteAsync(T message)
        {
            var writer = writeChannel.Writer;
            var writeRequest = new MessageWriteRequest(message);

            bool writeable = false;
            while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
            {
                if (writer.TryWrite(writeRequest))
                {
                    break;
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
        /// Writes the message and waits for the actual result. Slower than WriteAsync
        /// </summary>
        public async ValueTask<MessageWriteResult> WriteAndWaitAsync(T message)
        {
            var writer = writeChannel.Writer;
            var tcs = new TaskCompletionSource<MessageWriteResult>();
            var writeRequest = new MessageWriteRequest(message, tcs);

            bool writeable = false;
            while (writeable = await writer.WaitToWriteAsync().ConfigureAwait(false))
            {
                if (writer.TryWrite(writeRequest))
                {
                    break;
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

        #endregion

        #region Read/Write loops

        private async Task ReadLoopAsync()
        {
            var writer = readChannel.Writer;
            
            while(true)
            {
                var result = await base.ReadAsync().ConfigureAwait(false);

                if (result.IsCompleted)
                {
                    await writer.WriteAsync(result).ConfigureAwait(false);
                    break;
                }

                await writer.WriteAsync(result).ConfigureAwait(false);
            }

            writer.Complete();
        }

        private async Task WriteLoopAsync()
        {
            var reader = writeChannel.Reader;

            while (true)
            {
                var finished = await reader.WaitToReadAsync().ConfigureAwait(false);
                if (!finished || !reader.TryRead(out var writeRequest))
                {
                    break;
                }

                var writeResult = await base.WriteAsync(writeRequest.message).ConfigureAwait(false);
                if (writeRequest.tcs != null)
                {
                    writeRequest.tcs.TrySetResult(writeResult);
                }
            }

            while(reader.TryRead(out var writeRequest))
            {
                var writeResult = await base.WriteAsync(writeRequest.message).ConfigureAwait(false);
                if (writeRequest.tcs != null)
                {
                    writeRequest.tcs.TrySetResult(writeResult);
                }
            }
        }

        #endregion

        internal struct MessageWriteRequest
        {

            public T message;
            public TaskCompletionSource<MessageWriteResult> tcs;

            public MessageWriteRequest(T message, TaskCompletionSource<MessageWriteResult> tcs = null) : this()
            {
                this.message = message;
                this.tcs = tcs;
            }

        }

    }
}
