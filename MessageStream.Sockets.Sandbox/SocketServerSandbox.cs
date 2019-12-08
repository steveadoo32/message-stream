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

        private SocketServer<object, SimpleMessage> server;

        private Stopwatch stopwatch;
        private int messagesReceived = 0;

        public int Port { get; }

        public bool DebugReceived { get; set; } = false;

        public SocketServerSandbox(int port)
        {
            Port = port;
        }

        public async Task StartAsync()
        {
            stopwatch = new Stopwatch();

            server = new SocketServer<object, SimpleMessage>(
                Deserializer,
                Serializer,
                CreateConnectionState,
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

        private object CreateConnectionState(SocketServer<object, SimpleMessage>.Connection connection)
        {
            return new object();
        }

        async ValueTask HandleConnection(SocketServer<object, SimpleMessage>.Connection connection)
        {
            Logger.Info($"Client connected to server: {connection.Id}");
        }

        private Random random = new Random();
        async ValueTask<bool> HandleServerMessage(SocketServer<object, SimpleMessage>.Connection connection, SimpleMessage message)
        {
            int messageId = Interlocked.Increment(ref messagesReceived);

            if (messageId == 1)
            {
                stopwatch.Start();
            }
            if (messageId % 50000 == 0)
            {
                Logger.Info($"Messages received: {messagesReceived}. Messages/s: {messagesReceived / stopwatch.Elapsed.TotalSeconds}");
            }

            // if messages are being handled synchronously in the event message stream this can deadlock!
            if (message.Disconnect)
            {
                await connection.DisconnectAsync().ConfigureAwait(false);
            }

            if (DebugReceived)
            {
                Logger.Info($"Received message id {message.Id}");
            }

            if (!message.DontReply)
            {
                await connection.WriteAsync(new SimpleMessage
                {
                    Id = message.Id,
                    Value = messageId
                }).ConfigureAwait(false);

                if (DebugReceived)
                {
                    Logger.Info($"Replied for message id {message.Id}");
                }
            }

            return true;
        }

        ValueTask HandleServerDisconnection(SocketServer<object, SimpleMessage>.Connection connection, Exception ex, bool expected)
        {
            Logger.Info($"Client disconnected from server: {connection.Id}:{expected}. {ex}");
            return new ValueTask();
        }

        ValueTask HandleServerKeepAlive(SocketServer<object, SimpleMessage>.Connection connection)
        {
            return new ValueTask();
        }
    }
}
