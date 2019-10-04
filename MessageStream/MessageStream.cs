using MessageStream.IO;
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
        
        private readonly IReader reader;
        private readonly IMessageDeserializer<T> deserializer;
        private readonly IWriter writer;
        private readonly IMessageSerializer<T> serializer;

        private readonly PipeOptions readerPipeOptions;

        private readonly PipeOptions writerPipeOptions;
        private readonly TimeSpan writerCloseTimeout;

        private Pipe readPipe;
        private Pipe writePipe;

        private Task<bool> readTask;
        private Task<bool> writeTask;

        private CancellationTokenSource readCancellationTokenSource;
        private CancellationTokenSource writeCancellationTokenSource;

        private Exception readException;
        private Exception writeException;
        
        public bool Open { get; private set; }

        protected IMessageDeserializer<T> Deserializer => deserializer;

        protected IMessageSerializer<T> Serializer => serializer;

        /// <summary>
        /// </summary>
        /// <param name="writerCloseTimeout">How long should we wait for the writer to finish writing data before closing</param>
        public MessageStream(
            IReader reader,
            IMessageDeserializer<T> deserializer,
            IWriter writer,
            IMessageSerializer<T> serializer,
            PipeOptions readerPipeOptions = null,
            PipeOptions writerPipeOptions = null,
            TimeSpan? writerCloseTimeout = null
        )
        {
            this.reader = reader;
            this.deserializer = deserializer;
            this.writer = writer;
            this.serializer = serializer;
            this.readerPipeOptions = readerPipeOptions ?? new PipeOptions();
            this.writerPipeOptions = writerPipeOptions ?? new PipeOptions();
            this.writerCloseTimeout = writerCloseTimeout ?? TimeSpan.FromSeconds(5);

        }

        #region Open/Close

        public virtual Task OpenAsync()
        {
            if (Open)
            {
                throw new Exception("MessageStream is already open");
            }

            readPipe = new Pipe(
                readerPipeOptions
            );
            writePipe = new Pipe(
                writerPipeOptions
            );
            
            readCancellationTokenSource = new CancellationTokenSource();
            writeCancellationTokenSource = new CancellationTokenSource();

            readTask = ReadLoopAsync();
            writeTask = WriteLoopAsync();

            // Check eagerly if any of the read/write tasks failed right away and throw their exceptions
            if (readTask.IsFaulted)
            {
                throw readTask.Exception;
            }

            if (writeTask.IsFaulted)
            {
                throw writeTask.Exception;
            }

            Open = true;

            return Task.CompletedTask;
        }

        public virtual async Task CloseAsync()
        {
            if (!Open)
            {
                throw new Exception("MessageStream is not open");
            }

            Open = false;

            readCancellationTokenSource.Cancel();
            await readTask.ConfigureAwait(false);
            readCancellationTokenSource = null;
            readTask = null;

            writePipe.Writer.Complete();
            writeCancellationTokenSource.CancelAfter(writerCloseTimeout);
            await writeTask.ConfigureAwait(false);
            writeCancellationTokenSource = null;
            writeTask = null;

            readPipe.Reader.Complete();
            writePipe.Reader.Complete();
        }

        #endregion

        #region Read/Write

        public virtual async ValueTask<MessageReadResult<T>> ReadAsync()
        {
            DateTime timeReceived = DateTime.UtcNow;

            bool partialMessage = false;
            T message = default;
            SequencePosition read = default;
            
            ReadResult result = await readPipe.Reader.ReadAsync().ConfigureAwait(false);

            // Try to read one full message.
            while (!Decode(result.Buffer, out read, out message))
            {
                // This case means we read a partial message, so try to read the rest
                if (!result.IsCompleted)
                {
                    readPipe.Reader.AdvanceTo(read, result.Buffer.End);
                    result = await readPipe.Reader.ReadAsync().ConfigureAwait(false);
                }
                // We didn't have enough data in the buffer, and the reader is closed so we can't read anymore, so mark it as a partial message.
                else
                {
                    partialMessage = true;
                    break;
                }
            }

            // Let the caller process the incoming buffer
            if (!partialMessage)
            {
                await ProcessIncomingBufferAsync(message, result.Buffer.Slice(result.Buffer.Start, read)).ConfigureAwait(false);
            }

            var completed = result.Buffer.Length == 0 || partialMessage;

            if (!completed)
            {
                readPipe.Reader.AdvanceTo(read);
            }

            DateTime parsedTime = DateTime.UtcNow;

            return new MessageReadResult<T>
            {
                IsCompleted = completed,
                Error = readException != null,
                Exception = readException,
                Result = message,
                ReadResult = !partialMessage,
                ReceivedTimeUtc = timeReceived,
                ParsedTimeUtc = parsedTime
            };
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Decode(in ReadOnlySequence<byte> buffer, out SequencePosition read, out T message)
        {
            return deserializer.Deserialize(in buffer, out read, out message);
        }

        public virtual async ValueTask<MessageWriteResult> WriteAsync(T message, bool flush = true)
        {
            if (!Open)
            {
                return new MessageWriteResult
                {
                    IsCompleted = true,
                    Error = true,
                    Exception = null
                };
            }

            // Serialize
            var serializedMessage = SerializeMessage(message);

            await ProcessOutgoingBufferAsync(message, serializedMessage).ConfigureAwait(false);

            // Write the data into the Writer
            var result = await writePipe.Writer.WriteAsync(serializedMessage).ConfigureAwait(false);

            if (flush)
            {
                await writePipe.Writer.FlushAsync().ConfigureAwait(false);
            }

            return new MessageWriteResult
            {
                IsCompleted = result.IsCompleted,
                Error = writeException != null,
                Exception = writeException
            };
        }

        public ValueTask<FlushResult> FlushAsync()
        {
            return writePipe.Writer.FlushAsync();
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
            if (serializer.TryCalculateMessageSize(message, out size))
            {
                memory = writePipe.Writer.GetMemory(size);

                // We don't need to copy here because we are writing directly to the memory span
                buffer = serializer.Serialize(message, memory.Span.Slice(0, size), true);
            }
            else
            {
                // We need to write the returned span to the memory because the serializer method should've created it's own.
                buffer = serializer.Serialize(message, default, false);
                size = buffer.Length;

                // Request memory from the writer and copy the span to it
                memory = writePipe.Writer.GetMemory(size);
                buffer.CopyTo(memory.Span);
            }

            return memory.Slice(0, size);
        }

        #endregion

        #region Read/Write Loops

        private async Task<bool> ReadLoopAsync()
        {
            var cancellationToken = readCancellationTokenSource.Token;
            bool cleanStop = false;
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Request a block of memory from the readPipe's Writer
                    var memory = readPipe.Writer.GetMemory(readerPipeOptions.MinimumSegmentSize);

                    // Let the IReader read into the memory, returns how many bytes were actually read.
                    int len = await reader.ReadAsync(memory, cancellationToken).ConfigureAwait(false);

                    // If a reader returns len 0 then we should close the reader.
                    if (len == 0)
                    {
                        break;
                    }
                    
                    // Write the data into the Writer
                    await readPipe.Writer.WriteAsync(memory.Slice(0, len), cancellationToken).ConfigureAwait(false);

                    // Flush
                    await readPipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                cleanStop = cancellationToken.IsCancellationRequested;
                if (!cleanStop)
                {
                    readException = ex;
                }
            }
            
            readCancellationTokenSource.Cancel();
            readPipe.Writer.Complete();

            return cleanStop;
        }

        private async Task<bool> WriteLoopAsync()
        {
            var cancellationToken = writeCancellationTokenSource.Token;
            bool cleanStop = false;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Read from the writePipe's Reader pipe
                    ReadResult result = await writePipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                    // Write each buffer
                    foreach (var buffer in result.Buffer)
                    {
                        await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }

                    // Complete the write
                    writePipe.Reader.AdvanceTo(result.Buffer.End);

                    // Flush the rest of the data, then close.
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                cleanStop = cancellationToken.IsCancellationRequested;
                
                if (!cleanStop)
                {
                    writeException = ex;
                    writeCancellationTokenSource.Cancel();
                }
                
                writePipe.Reader.Complete();
            }
            
            return cleanStop;
        }

        #endregion

        #region Virtual methods
        
        /// <summary>
        /// Optionally processes an incoming buffer and it's associated message. The provided ReadOnlySequence is JUST the messages data,
        /// This allows consumers to provide their own way of allocating memory for processing.
        /// 
        /// Useful for journaling incoming data or something like that.
        /// </summary>
        protected virtual ValueTask ProcessIncomingBufferAsync(T message, ReadOnlySequence<byte> buffer)
        {
            return new ValueTask();
        }

        /// <summary>
        /// Optionally processes an outgoing buffer and it's associated message.
        /// 
        /// Useful for journaling outgoing data or something like that.
        /// </summary>
        protected virtual ValueTask ProcessOutgoingBufferAsync(T message, Memory<byte> buffer)
        {
            return new ValueTask();
        }

        #endregion

    }
}