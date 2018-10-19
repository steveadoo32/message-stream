using MessageStream.IO;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Server
{
    internal class ClientSocketReaderWriter : IReader, IWriter
    {

        private readonly Socket socket;

        public ClientSocketReaderWriter(Socket socket)
        {
            this.socket = socket;
        }

        public Task DisconnectAsync()
        {
            socket.Disconnect(false);
            socket.Dispose();

            return Task.CompletedTask;
        }

        ValueTask<int> IReader.ReadAsync(Memory<byte> memory, CancellationToken cancellationToken)
        {
            return socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
        }

        ValueTask<int> IWriter.WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            return socket.SendAsync(memory, SocketFlags.None, cancellationToken);
        }

    }
}