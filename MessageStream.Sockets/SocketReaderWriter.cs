using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MessageStream.IO;

namespace MessageStream.Sockets
{
    internal class SocketReaderWriter : IReader, IWriter
    {

        private Socket socket;
        
        public async Task ConnectAsync(SocketConfiguration socketConfiguration)
        {
            socket = new Socket(socketConfiguration.AddressFamily, socketConfiguration.SocketType, socketConfiguration.ProtocolType);

            await socket.ConnectAsync(socketConfiguration.Ip, socketConfiguration.Port).ConfigureAwait(false);
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