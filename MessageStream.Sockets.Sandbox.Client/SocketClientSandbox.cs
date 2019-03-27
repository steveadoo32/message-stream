using MessageStream.ProtoBuf;
using MessageStream.Sockets.Server;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Sandbox
{
    public class SocketClientSandbox
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static readonly SimpleMessageDeserializer Deserializer = new SimpleMessageDeserializer();
        public static readonly SimpleMessageSerializer Serializer = new SimpleMessageSerializer();

        private EventSocketMessageStream<SimpleMessage> clientStream;

        public string Ip { get; }

        public int Port { get; }

        public SocketClientSandbox(string ip, int port)
        {
            Ip = ip;
            Port = port;
        }

        public async Task StartAsync()
        {
            var config = new SocketConfiguration
            {
                Ip = Ip,
                Port = Port
            };

            clientStream = new EventSocketMessageStream<SimpleMessage>(
                config,
                Deserializer,
                Serializer,
                HandleClientMessage,
                HandleClientDisconnection,
                HandleClientKeepAlive,
                1,
                true);

            await clientStream.OpenAsync().ConfigureAwait(false);
        }

        public async Task SendMessagesAsync()
        {
            for (int i = 0; i < 1000000; i++)
            {
                await clientStream.WriteAsync(new SimpleMessage
                {
                    Id = 1,
                    Value = 5
                }).ConfigureAwait(false);
                await Task.Delay(16).ConfigureAwait(false);
            }
        }

        public async Task StopAsync()
        {
            await clientStream.CloseAsync().ConfigureAwait(false);
        }

        async ValueTask<bool> HandleClientMessage(SimpleMessage message)
        {
            // Logger.Info($"Client message received: {message}");

            return true;
        }

        ValueTask HandleClientDisconnection(Exception ex, bool expected)
        {
            Logger.Info($"Client disconnected");
            return new ValueTask();
        }

        ValueTask HandleClientKeepAlive()
        {
            return new ValueTask();
        }

    }
}
