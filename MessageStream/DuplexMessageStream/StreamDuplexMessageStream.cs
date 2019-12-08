using Pipelines.Sockets.Unofficial;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.DuplexMessageStream
{

    public delegate ValueTask<Stream> StreamProviderAsync(CancellationToken cancellationToken);

    public class StreamDuplexMessageStream : IDuplexMessageStream
    {

        private readonly StreamProviderAsync readProvider;
        private readonly StreamProviderAsync writeProvider;

        private Stream readStream;
        private Stream writeStream;
        private PipeReader reader;
        private PipeWriter writer;

        public bool ReadCompleted { get; private set; }

        public bool WriteCompleted { get; private set; }

        public string StatsString => reader == null ? "{ open=false }" : $"{{ open=true }}";

        public StreamDuplexMessageStream(StreamProviderAsync provider)
        {
            this.readProvider = provider;
            this.writeProvider = provider;
        }

        public StreamDuplexMessageStream(Stream stream)
            : this(cancellationToken => new ValueTask<Stream>(stream))
        {
        }

        public StreamDuplexMessageStream(StreamProviderAsync readProvider, StreamProviderAsync writeProvider)
        {
            this.readProvider = readProvider;
            this.writeProvider = writeProvider;
        }

        public StreamDuplexMessageStream(Stream readStream, Stream writeStream)
            : this(cancellationToken => new ValueTask<Stream>(readStream), cancellationToken => new ValueTask<Stream>(writeStream))
        {
        }

        async Task IDuplexMessageStream.OpenAsync(CancellationToken cancellationToken)
        {
            ReadCompleted = false;
            WriteCompleted = false;

            readStream = await readProvider(cancellationToken).ConfigureAwait(false);
            writeStream = writeProvider == readProvider ? readStream : await writeProvider(cancellationToken).ConfigureAwait(false);
            reader = StreamConnection.GetReader(readStream);
            writer = StreamConnection.GetWriter(writeStream);
        }

        async Task IDuplexMessageStream.CloseAsync()
        {
            await reader.CompleteAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);

            readStream.Close();
            if (writeStream != readStream)
            {
                writeStream.Close();
            }

            ReadCompleted = true;
            WriteCompleted = true;
            readStream = null;
            writeStream = null;
            reader = null;
            writer = null;
        }

        async ValueTask<ReadResult> IDuplexMessageStream.ReadAsync(CancellationToken cancellationToken)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                ReadCompleted = true;
            }

            return result;
        }

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed, SequencePosition examined) => reader.AdvanceTo(consumed, examined);

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed) => reader.AdvanceTo(consumed);

        Memory<byte> IDuplexMessageStream.GetWriteMemory(int sizeHint) => writer.GetMemory(sizeHint);

        async ValueTask<FlushResult> IDuplexMessageStream.WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            var result = await writer.WriteAsync(memory, cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                WriteCompleted = true;
            }

            return result;
        }

        async ValueTask<FlushResult> IDuplexMessageStream.FlushWriterAsync(CancellationToken cancellationToken)
        {
            var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                WriteCompleted = true;
            }

            return result;
        }


    }
}
