using MessageStream.ProtoBuf;
using MessageStream.Sockets.Server;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MessageStream.Sockets.Tests.Tests
{
    public class SocketTests
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static readonly ProtoBufMessageDeserializer Deserializer = new ProtoBufMessageDeserializer();
        public static readonly ProtoBufMessageSerializer Serializer = new ProtoBufMessageSerializer();

        [Fact(DisplayName = "Socket Reads Data")]
        public async Task SocketReadsData()
        {
            ValueTask HandleConnection(SocketServer<object, object>.Connection connection)
            {
                Logger.Info($"Client connected to server: {connection.Id}");
                return new ValueTask();
            }

            ValueTask<bool> HandleServerMessage(SocketServer<object, object>.Connection connection, object message)
            {
                Logger.Info($"Server message received: {connection.Id}:{message}");
                return new ValueTask<bool>();
            }

            ValueTask HandleServerDisconnection(SocketServer<object, object>.Connection connection, Exception ex, bool expected)
            {
                Logger.Info($"Client disconnected from server: {connection.Id}:{expected}. {ex}");
                return new ValueTask();
            }

            ValueTask HandleServerKeepAlive(SocketServer<object, object>.Connection connection)
            {
                Logger.Info($"Server keep handled alive for: {connection.Id}");
                return new ValueTask();
            }

            var server = new SocketServer<object, object>(
                Deserializer,
                Serializer,
                null,
                HandleConnection,
                HandleServerMessage,
                HandleServerDisconnection,
                HandleServerKeepAlive);

            await server.ListenAsync(5463).ConfigureAwait(false);

            await Task.Delay(1000).ConfigureAwait(false);

            ValueTask<bool> HandleClientMessage(object message)
            {
                Logger.Info($"Client message received: {message}");
                return new ValueTask<bool>();
            }

            ValueTask HandleClientDisconnection(Exception ex)
            {
                Logger.Info($"Client disconnected");
                return new ValueTask();
            }

            ValueTask HandleClientKeepAlive()
            {
                Logger.Info($"Handling client keep alive.");
                return new ValueTask();
            }

            var config = new SocketConfiguration
            {
                Ip = "127.0.0.1",
                Port = 5463
            };

            var clientStream = new EventMessageStream<object>(
                Deserializer,
                Serializer,
                new SocketDuplexMessageStreamWrapper(config),
                HandleClientMessage,
                HandleClientKeepAlive,
                HandleClientDisconnection);

            await clientStream.OpenAsync().ConfigureAwait(false);

            await clientStream.WriteAsync(new PingMessage
            {
                Id = 1
            }).ConfigureAwait(false);
        }

    }
}
