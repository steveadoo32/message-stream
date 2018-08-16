using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using System.Buffers;
using System.Runtime.CompilerServices;
using MessageStream.IO;
using MessageStream.Message;
using System.Threading;

namespace MessageStream
{
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

        /// <summary>
        /// 
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

        public Task OpenAsync()
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

        public async Task CloseAsync()
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

            readPipe.Reset();
            writePipe.Reset();
        }

        #endregion

        #region Read/Write
        
        public async ValueTask<MessageReadResult<T>> ReadAsync()
        {
            bool partialMessage = false;
            T message = default;
            SequencePosition read = default;

            ReadResult result = await readPipe.Reader.ReadAsync().ConfigureAwait(false);
            
            // If the result is completed we could still have data in the buffer that we have try to read.
            while (!Decode(result.Buffer, out read, out message))
            {
                if (!result.IsCompleted)
                {
                    readPipe.Reader.AdvanceTo(read, result.Buffer.End);
                    result = await readPipe.Reader.ReadAsync().ConfigureAwait(false);
                }
                else
                {
                    // We didn't have enough data in the buffer, and the reader is closed so we can't read anymore, so really mark it as complete now.
                    partialMessage = true;
                    break;
                }
            }
            
            readPipe.Reader.AdvanceTo(read);
            
            return new MessageReadResult<T>
            {
                IsCompleted = result.Buffer.Length == 0 || partialMessage,
                Error = readException != null,
                Exception = readException,
                Result = message
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Decode(in ReadOnlySequence<byte> buffer, out SequencePosition read, out T message)
        {
            return deserializer.Deserialize(in buffer, out read, out message);
        }

        public async ValueTask<MessageWriteResult> WriteAsync(T message)
        {
            // Write the data into the Writer
            var result = await writePipe.Writer.WriteAsync(SerializeMessage(message)).ConfigureAwait(false);

            await writePipe.Writer.FlushAsync().ConfigureAwait(false);

            return new MessageWriteResult
            {
                IsCompleted = result.IsCompleted,
                Error = writeException != null,
                Exception = writeException
            };
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

                // Only complete the reader if there was an error writing.
                // We do this so when we close the stream we can flush the rest of the bytes in the write pipe to the IWriter.
                writePipe.Reader.Complete();
            }
            
            return cleanStop;
        }

        #endregion

    }
}