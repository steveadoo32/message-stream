using MessageStream.ProtoBuf;
using MessageStream.Sockets.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Sandbox
{
    public class SocketServerSandbox
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static readonly SimpleMessageDeserializer Deserializer = new SimpleMessageDeserializer();
        public static readonly SimpleMessageSerializer Serializer = new SimpleMessageSerializer();

        private SocketServer<SimpleMessage> server;

        private Stopwatch stopwatch;
        private int messagesReceived = 0;

        public int Port { get; }

        public SocketServerSandbox(int port)
        {
            Port = port;
        }

        public async Task StartAsync()
        {
            stopwatch = new Stopwatch();

            server = new SocketServer<SimpleMessage>(
                Deserializer,
                Serializer,
                HandleConnection,
                HandleServerMessage,
                HandleServerDisconnection,
                HandleServerKeepAlive);

            await server.ListenAsync(Port).ConfigureAwait(false);
        }   
        
        public async Task StopAsync()
        {
            await server.CloseAsync().ConfigureAwait(false);
        }

        ValueTask HandleConnection(SocketServer<SimpleMessage>.Connection connection)
        {
            Logger.Info($"Client connected to server: {connection.Id}");
            return new ValueTask();
        }

        async ValueTask<bool> HandleServerMessage(SocketServer<SimpleMessage>.Connection connection, SimpleMessage message)
        {
            // Logger.Info($"Server message received: {connection.Id}:{message}");

            int messageId = Interlocked.Increment(ref messagesReceived);

            if (messageId == 1)
            {
                stopwatch.Start();
            }

            if (messageId % 1000000 == 0)
            {
                Logger.Info($"Messages received: {messagesReceived}. Messages/s: {messagesReceived / stopwatch.Elapsed.TotalSeconds}");
            }

            Deserializer.messageProvider.Return(0, message);

            return true;
        }

        ValueTask HandleServerDisconnection(SocketServer<SimpleMessage>.Connection connection, Exception ex, bool expected)
        {
            Logger.Info($"Client disconnected from server: {connection.Id}:{expected}. {ex}");
            return new ValueTask();
        }

        ValueTask HandleServerKeepAlive(SocketServer<SimpleMessage>.Connection connection)
        {
            return new ValueTask();
        }
    }
}
