using Pipelines.Sockets.Unofficial;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.DuplexMessageStream
{

    public delegate ValueTask<SocketConnection> SocketConnectionProviderAsync(CancellationToken cancellationToken);

    public delegate ValueTask<Socket> SocketProviderAsync(CancellationToken cancellationToken);

    public class SocketDuplexMessageStream : IDuplexMessageStream
    {

        private readonly SocketConnectionProviderAsync provider;
        private SocketConnection connection;

        public bool ReadCompleted { get; private set; }

        public bool WriteCompleted { get; private set; }

        public string StatsString => connection == null ? "{ open=false }" : $"{{ connected={connection.Socket.Connected}, bytesRead={connection.BytesRead}, bytesSent={connection.BytesSent} }}";

        public SocketDuplexMessageStream(SocketConnectionProviderAsync provider)
        {
            this.provider = provider;
        }

        public SocketDuplexMessageStream(SocketProviderAsync provider)
            : this(async (token) => SocketConnection.Create(await provider(token).ConfigureAwait(false)))
        {
        }

        async Task IDuplexMessageStream.OpenAsync(CancellationToken cancellationToken)
        {
            ReadCompleted = false;
            WriteCompleted = false;

            connection = await provider(cancellationToken).ConfigureAwait(false);
        }

        async Task IDuplexMessageStream.CloseAsync()
        {
            await connection.Input.CompleteAsync().ConfigureAwait(false);
            await connection.Output.CompleteAsync().ConfigureAwait(false);

            connection.Dispose();

            ReadCompleted = true;
            WriteCompleted = true;
            connection = null;
        }

        async ValueTask<ReadResult> IDuplexMessageStream.ReadAsync(CancellationToken cancellationToken)
        {
            var result = await connection.Input.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                ReadCompleted = true;
            }

            return result;
        }

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed, SequencePosition examined) => connection.Input.AdvanceTo(consumed, examined);

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed) => connection.Input.AdvanceTo(consumed);

        Memory<byte> IDuplexMessageStream.GetWriteMemory(int sizeHint) => connection.Output.GetMemory(sizeHint);

        async ValueTask<FlushResult> IDuplexMessageStream.WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            var result = await connection.Output.WriteAsync(memory, cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                WriteCompleted = true;
            }

            return result;
        }

        async ValueTask<FlushResult> IDuplexMessageStream.FlushWriterAsync(CancellationToken cancellationToken)
        {
            var result = await connection.Output.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                WriteCompleted = true;
            }

            return result;
        }


    }
}
