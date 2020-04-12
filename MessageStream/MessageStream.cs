using MessageStream.Message;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream
{

    /// <summary>
    /// TODO, its not ideal that you HAVE to have a both a writer and a reader, and not just one or the other.
    /// We could probably provide different constructors that null out the writers/readers but its a lot of 
    /// ugly logic to put in.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class MessageStream<T>
    {

        private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        protected readonly MessageStreamOptions options;

        private readonly MemoryPool<MessageReadResult<T>> batchReadPool;
        private readonly IDuplexMessageStream duplexMessageStream;

        private CancellationTokenSource closeCts;

        protected IMessageDeserializer<T> Deserializer { get; private set; }

        protected IMessageSerializer<T> Serializer { get; private set; }

        public bool Open { get; private set; }

        public MessageStreamReadStats ReadStats { get; } = new MessageStreamReadStats();

        public MessageStreamWriteStats WriteStats { get; } = new MessageStreamWriteStats();

        public string DuplexStreamStats => duplexMessageStream.StatsString;

        /// <summary>
        /// </summary>
        /// <param name="writerCloseTimeout">How long should we wait for the writer to finish writing data before closing</param>
        public MessageStream(
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
            IDuplexMessageStream duplexMessageStream,
            MessageStreamOptions options = null,
            MemoryPool<MessageReadResult<T>> batchReadPool = null
        )
        {
            this.Deserializer = deserializer;
            this.Serializer = serializer;
            this.duplexMessageStream = duplexMessageStream;
            this.options = options ?? new MessageStreamOptions();
            this.batchReadPool = batchReadPool ?? MemoryPool<MessageReadResult<T>>.Shared;
        }

        #region Open/Close

        public virtual async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            if (Open)
            {
                throw new MessageStreamOpenException("MessageStream already open");
            }

            Logger.Trace("Opening message stream.");

            closeCts = new CancellationTokenSource();

            ReadStats.Reset();
            WriteStats.Reset();

            try
            {
                await duplexMessageStream.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Cleanup();
                Logger.Error(ex, "Error opening message stream.");
                throw new MessageStreamOpenException("Error opening duplex message stream", ex);
            }

            Open = true;

            Logger.Trace("Opened message stream.");
        }

        public virtual async Task CloseAsync()
        {
            if (!Open)
            {
                throw new MessageStreamCloseException("MessageStream already closed.");
            }

            Logger.Info("Closing message stream.");

            closeCts.Cancel();

            try
            {
                await duplexMessageStream.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error message stream.");

                throw new MessageStreamOpenException("Error closing duplex message stream", ex);
            }
            finally
            {
                Cleanup();
                Open = false;
                Logger.Info("Closed message stream.");
            }
        }

        private void Cleanup()
        {
            closeCts.Dispose();
            ReadStats.Reset();
            WriteStats.Reset();
        }

        #endregion

        #region Read/Write

        public virtual async ValueTask<MessageReadResult<T>> ReadAsync()
        {
            DateTime timeReceived = DateTime.UtcNow;

            bool partialMessage = false;
            // Try to read one full message.
            try
            {
                T message = default;
                SequencePosition read = default;

                var closeToken = closeCts?.Token ?? default;
                ReadResult result = await duplexMessageStream.ReadAsync(closeToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                while (!Deserializer.Deserialize(in buffer, out read, out message))
                {
                    // This case means we read a partial message, so try to read the rest
                    if (!result.IsCompleted)
                    {
                        duplexMessageStream.AdvanceReaderTo(read, result.Buffer.End);
                        ReadStats.IncrBytesRead(result.Buffer.Length);
                        result = await duplexMessageStream.ReadAsync(closeToken).ConfigureAwait(false);
                        buffer = result.Buffer;
                    }
                    // We didn't have enough data in the buffer, and the reader is closed so we can't read anymore, so mark it as a partial message.
                    else
                    {
                        partialMessage = true;
                        break;
                    }
                }

                // Track the time it took to parse
                DateTime parsedTimeUtc = DateTime.UtcNow;

                if (!partialMessage)
                {
                    // not sure how expensive this is
                    var slicedBuffer = result.Buffer.Slice(result.Buffer.Start, read);

                    ReadStats.IncMessagesRead();
                    ReadStats.IncrBytesRead(slicedBuffer.Length);

                    try
                    {
                        ReadStats.IncMessagesIncomingBufferProcessing(1);
                        await ProcessIncomingBufferAsync(message, slicedBuffer).ConfigureAwait(false);
                        ReadStats.DecMessagesIncomingBufferProcessing(1);
                    }
                    catch (Exception ex)
                    {
                        ReadStats.DecMessagesIncomingBufferProcessing(1);
                        Logger.Error(ex, "Error processing incoming message buffer.");
                    }
                }

                if (!partialMessage)
                {

                    duplexMessageStream.AdvanceReaderTo(read);
                }

                DateTime parsedTime = DateTime.UtcNow;
                return new MessageReadResult<T>
                {
                    // if the stream is completed, we can still try to read more messages, so we use the partialMessage field to indicate that.
                    IsCompleted = result.IsCompleted && partialMessage,
                    Error = false,
                    Exception = null,
                    Result = message,
                    ReadResult = !partialMessage,
                    ReceivedTimeUtc = timeReceived,
                    ParsedTimeUtc = parsedTimeUtc
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reading message from duplex message stream.");

                return new MessageReadResult<T>
                {
                    IsCompleted = duplexMessageStream.ReadCompleted,
                    Error = true,
                    Exception = ex,
                    Result = default,
                    ReadResult = !partialMessage,
                    ReceivedTimeUtc = timeReceived,
                    ParsedTimeUtc = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Reads up to batchSize messages in the next available buffer.
        /// note: its not guaranteed that the buffer has batchSize messages in it.
        /// </summary>
        /// <param name="batchSize"></param>
        /// <returns></returns>
        public virtual async ValueTask<IMemoryOwner<MessageReadResult<T>>> ReadBatchAsync(int batchSize = 16)
        {
            DateTime timeReceived = DateTime.UtcNow;

            // Try to read one full message.
            var memory = batchReadPool.Rent(batchSize);
            try
            {
                T message = default;
                SequencePosition read = default;

                var closeToken = closeCts?.Token ?? default;
                ReadResult result = await duplexMessageStream.ReadAsync(closeToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                int index = 0;
                while (index < batchSize)
                {
                    if (!Deserializer.Deserialize(in buffer, out read, out message))
                    {
                        if (result.IsCompleted)
                        {
                            memory.Memory.Span[index] = new MessageReadResult<T>
                            {
                                // if the stream is completed, we can still try to read more messages, so we use the partialMessage field to indicate that.
                                IsCompleted = result.IsCompleted,
                                Error = false,
                                Exception = null,
                                Result = default,
                                ReadResult = false,
                                ReceivedTimeUtc = timeReceived,
                                ParsedTimeUtc = DateTime.UtcNow
                            };
                        }
                        break;
                    }

                    // not sure how expensive this is
                    var slicedBuffer = buffer.Slice(buffer.Start, read);

                    ReadStats.IncMessagesRead();
                    ReadStats.IncrBytesRead(slicedBuffer.Length);

                    try
                    {
                        ReadStats.IncMessagesIncomingBufferProcessing(1);
                        await ProcessIncomingBufferAsync(message, slicedBuffer).ConfigureAwait(false);
                        ReadStats.DecMessagesIncomingBufferProcessing(1);
                    }
                    catch (Exception ex)
                    {
                        ReadStats.DecMessagesIncomingBufferProcessing(1);
                        Logger.Error(ex, "Error processing incoming message buffer.");
                    }

                    memory.Memory.Span[index] = new MessageReadResult<T>
                    {
                        // if the stream is completed, we can still try to read more messages, so we use the partialMessage field to indicate that.
                        IsCompleted = result.IsCompleted,
                        Error = false,
                        Exception = null,
                        Result = message,
                        ReadResult = true,
                        ReceivedTimeUtc = timeReceived,
                        ParsedTimeUtc = DateTime.UtcNow
                    };

                    buffer = buffer.Slice(read);
                    index++;
                }

                // Mark the rest as empty
                for(int i = index; i < batchSize; i++)
                {
                    memory.Memory.Span[i] = new MessageReadResult<T>
                    {
                        IsEmpty = true
                    };
                }

                // Track the time it took to parse
                if (!result.IsCompleted)
                {
                    duplexMessageStream.AdvanceReaderTo(read);
                }

                return memory;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reading message from duplex message stream.");

                memory.Memory.Span[0] = new MessageReadResult<T>
                {
                    IsCompleted = duplexMessageStream.ReadCompleted,
                    Error = true,
                    Exception = ex,
                    Result = default,
                    ReadResult = false,
                    ReceivedTimeUtc = timeReceived,
                    ParsedTimeUtc = DateTime.UtcNow
                };

                return memory;
            }
        }

        public virtual async ValueTask<MessageWriteResult> WriteAsync(T message, bool flush = true)
        {
            try
            {
                // Serialize
                var serializedMessage = SerializeMessage(message);

                WriteStats.IncMessagesOutgoingBufferProcessing();
                try
                {
                    await ProcessOutgoingBufferAsync(message, serializedMessage).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error processing outgoing message buffer.");
                }

                WriteStats.DecMessagesOutgoingBufferProcessing(1);
                WriteStats.IncMessagesWriting();

                // Write the data into the Writer
                var cancellationToken = closeCts?.Token ?? default;
                var result = await duplexMessageStream.WriteAsync(serializedMessage, cancellationToken).ConfigureAwait(false);

                WriteStats.IncMessagesWritten();
                WriteStats.IncrBytesWritten(serializedMessage.Length);

                if (flush && !result.IsCompleted)
                {
                    result = await FlushAsync().ConfigureAwait(false);
                }

                WriteStats.DecMessagesWriting(1);

                return new MessageWriteResult
                {
                    IsCompleted = result.IsCompleted,
                    Error = false,
                    Exception = null
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error writing message to duplex message stream.");

                WriteStats.DecMessagesWriting(1);

                return new MessageWriteResult
                {
                    IsCompleted = duplexMessageStream.WriteCompleted,
                    Error = true,
                    Exception = ex
                };
            }
        }

        public virtual async ValueTask<FlushResult> FlushAsync()
        {
            var cancellationToken = closeCts?.Token ?? default;

            WriteStats.Flushing = true;
            var result = await duplexMessageStream.FlushWriterAsync(cancellationToken).ConfigureAwait(false);
            WriteStats.Flushing = false;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Memory<byte> SerializeMessage(T message)
        {
            // The memory we are sending to the writer
            Memory<byte> memory = default;
            // The messages buffer
            Span<byte> buffer = default;
            // Message size.
            int size = 0;

            // Check if we can set a fixed size. We can write directly to the backing memory if it's fixed
            if (Serializer.TryCalculateMessageSize(message, out size))
            {
                memory = duplexMessageStream.GetWriteMemory(size);

                // We don't need to copy here because we are writing directly to the memory span
                buffer = Serializer.Serialize(message, memory.Span.Slice(0, size), true);
            }
            else
            {
                // We need to write the returned span to the memory because the serializer method should've created it's own.
                buffer = Serializer.Serialize(message, default, false);
                size = buffer.Length;

                // Request memory from the writer and copy the span to it
                memory = duplexMessageStream.GetWriteMemory(size);
                buffer.CopyTo(memory.Span);
            }

            return memory.Slice(0, size);
        }

        #endregion

        #region Hooks

        protected virtual ValueTask ProcessIncomingBufferAsync(T message, ReadOnlySequence<byte> buffer) => new ValueTask();

        protected virtual ValueTask ProcessOutgoingBufferAsync(T message, Memory<byte> buffer) => new ValueTask();

        #endregion

        public override string ToString()
        {
            return $"{{ hashCode={this.GetHashCode()}, readStats={ReadStats}, writeStats={WriteStats} }}";
        }

    }
}