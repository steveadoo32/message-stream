using MessageStream.Message;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Server
{
    public class SocketServer<TConnectionState, TMessage>
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate ValueTask<TConnectionState> HandleConnectionAsync(Connection connection);

        public delegate ValueTask<bool> HandleConnectionMessageAsync(Connection connection, TMessage message);

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate ValueTask HandleConnectionDisconnectionAsync(Connection connection, Exception ex, bool expected);

        public delegate ValueTask HandleConnectionKeepAliveAsync(Connection connection);

        private readonly HandleConnectionAsync handleConnectionDelegate;
        private readonly HandleConnectionMessageAsync handleConnectionMessageDelegate;
        private readonly HandleConnectionDisconnectionAsync handleConnectionDisconnectionDelegate;
        private readonly HandleConnectionKeepAliveAsync handleConnectionKeepAliveDelegate;

        private readonly IMessageDeserializer<TMessage> deserializer;
        private readonly IMessageSerializer<TMessage> serializer;

        private Socket socketListener;

        private int connectionIdCounter = 0;

        private CancellationTokenSource acceptCts;
        private Task acceptTask;

        private ConcurrentDictionary<int, Connection> Connections { get; set; }

        public SocketServer(
            IMessageDeserializer<TMessage> deserializer,
            IMessageSerializer<TMessage> serializer,
            HandleConnectionAsync handleConnectionDelegate,
            HandleConnectionMessageAsync handleMessageDelegate,
            HandleConnectionDisconnectionAsync handleConnectionDisconnectionDelegate,
            HandleConnectionKeepAliveAsync handleKeepAliveDelegate
        )
        {
            this.deserializer = deserializer;
            this.serializer = serializer;

            this.handleConnectionDelegate = handleConnectionDelegate;
            this.handleConnectionMessageDelegate = handleMessageDelegate;
            this.handleConnectionDisconnectionDelegate = handleConnectionDisconnectionDelegate;
            this.handleConnectionKeepAliveDelegate = handleKeepAliveDelegate;
        }

        public Task ListenAsync(int port, int maxPendingConnections = 1000)
        {
            // TODO do we want to be able to listen on multiple ports? we should support that.
            Logger.Info($"Starting server...");

            Connections = new ConcurrentDictionary<int, Connection>();
            
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);

            // Create a TCP/IP socket.  
            socketListener = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            socketListener.Bind(localEndPoint);
            socketListener.Listen(maxPendingConnections);

            Logger.Info($"Listening on {port} {{ maxPendingConnections={maxPendingConnections} }}.");

            acceptCts = new CancellationTokenSource();

            // Pass the token to the wrapper task,
            // AcceptAsync doesnt support a cancellation token so we have to cancel the outer task.
            acceptTask = Task.Run(AcceptLoopAsync, acceptCts.Token); // TODO can you run this on multiple threads?

            Logger.Debug($"Accept loop started.");
            
            return Task.CompletedTask;
        }

        public async Task CloseAsync()
        {
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error shutting down accept loop.");
            }

            socketListener.Close();

            Logger.Info("Shutdown server.");
        }

        public Connection GetConnection(int connectionId)
        {
            if (Connections.TryGetValue(connectionId, out var value))
            {
                return value;
            }
            return null;
        }

        private ValueTask HandleKeepAliveAsync(Connection connection)
        {
            return handleConnectionKeepAliveDelegate(connection);
        }

        private async ValueTask HandleConnectionDisconnectAsync(Connection connection, Exception ex, bool expected)
        {
            try
            {
                await handleConnectionDisconnectionDelegate(connection, ex, expected).ConfigureAwait(false);
            }
            catch (Exception dcEx)
            {
                Logger.Error(dcEx, $"Error disconnecting connection {{ connectionId={connection.Id} }}");
            }
        }

        private ValueTask<bool> HandleMessagAsync(Connection connection, TMessage msg)
        {
            return handleConnectionMessageDelegate(connection, msg);
        }

        #region Accepting connections

        private async Task AcceptLoopAsync()
        {
            while (!acceptCts.IsCancellationRequested)
            {
                try
                {
                    var socket = await socketListener.AcceptAsync().ConfigureAwait(false);

                    // use Math.abs to fix overflows if they ever happen.
                    int connectionId = Math.Abs(Interlocked.Increment(ref connectionIdCounter));
                    var connection = new Connection(connectionId, socket);

                    Logger.Info($"Connection connected {{ connectionId={connectionId} }}.");

                    // We have a socket, setup the message stream
                    var messageStream = new ClientMessageStream<TMessage>(
                        socket,
                        deserializer,
                        serializer,
                        msg => HandleMessagAsync(connection, msg),
                        (ex, expected) => HandleConnectionDisconnectAsync(connection, ex, expected),
                        () => HandleKeepAliveAsync(connection),
                        1,
                        false
                    );

                    connection.MessageStream = messageStream;

                    if (!Connections.TryAdd(connectionId, connection))
                    {
                        // TODO handle this. Shouldn't ever happen, but we should just disconnect the client.
                        await connection.DisconnectAsync().ConfigureAwait(false);
                        Logger.Warn($"Could not add connection to connection list. Disconnected connection. {{ connectionId={connectionId} }}.");

                    }

                    await messageStream.OpenAsync().ConfigureAwait(false);

                    Logger.Debug($"MessageStream initialized {{ connectionId={connectionId} }}.");

                    connection.State = await handleConnectionDelegate(connection).ConfigureAwait(false);

                    Logger.Debug($"Connection initialized {{ connectionId={connectionId} }}.");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Accept loop exception.");

                    bool close = await HandleAcceptExceptionAsync(ex).ConfigureAwait(false);
                    if (close)
                    {
                        Logger.Info("Closing accept loop because of exception.");
                        break;
                    }
                }
            }
        }

        protected virtual async Task<bool> HandleAcceptExceptionAsync(Exception ex)
        {
            return false;
        }

        #endregion

        public class Connection
        {

            public Connection(int id, Socket socket)
            {
                Id = id;
                Socket = socket;
            }

            public int Id { get; }

            public TConnectionState State { get; internal set; }

            public bool GracefulDisconnect { get; private set; }

            internal Socket Socket { get; }

            internal ClientMessageStream<TMessage> MessageStream { get; set; }

            public async Task DisconnectAsync()
            {
                GracefulDisconnect = true;
                await MessageStream.CloseAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// NOTE: All writes will be reported as success because messages are dropped
            /// into a queue that are written later. If you would like to wait for an actual result on the write,
            /// use WriteAndWaitAsync
            /// </summary>
            public ValueTask<MessageWriteResult> WriteAsync(TMessage message)
            {
                return MessageStream.WriteAsync(message);
            }

            /// <summary>
            /// Writes the message and waits until it's actually been written to the pipe. Slower than WriteAsync
            /// </summary>
            public ValueTask<MessageWriteResult> WriteAndWaitAsync(TMessage message)
            {
                return MessageStream.WriteAndWaitAsync(message);
            }

            /// <summary>
            /// Writes a message and waits for a specific message to come back.
            /// </summary>
            public ValueTask<MessageWriteRequestResult<TReply>> WriteRequestAsync<TReply>(TMessage message, Func<TMessage, bool> matchFunc = null, int timeoutMilliseconds = -1) where TReply : TMessage
            {
                return MessageStream.WriteRequestAsync<TReply>(message, matchFunc, timeoutMilliseconds);
            }

        }

    }
}
