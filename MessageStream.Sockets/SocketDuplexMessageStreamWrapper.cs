using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MessageStream.DuplexMessageStream;
using MessageStream.IO;
using Pipelines.Sockets.Unofficial;

namespace MessageStream.Sockets
{

    public class SocketDuplexMessageStreamWrapper : SocketDuplexMessageStream
    {

        public SocketDuplexMessageStreamWrapper(SocketConfiguration configuration)
            : base(async cancellationToken =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                SocketConnection.SetRecommendedClientOptions(socket);
                await socket.ConnectAsync(configuration.Ip, configuration.Port).ConfigureAwait(false);

                return socket;
            })
        {
        }
    }

}