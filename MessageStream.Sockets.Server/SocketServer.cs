using MessageStream.Message;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using MessageStream.DuplexMessageStream;
using Microsoft.Extensions.Logging;

namespace MessageStream.Sockets.Server
{
    public class SocketServer<TConnectionState, TMessage>
    {

        public static EventMessageStreamOptions DefaultClientMessageStreamOptions => new EventMessageStreamOptions
        {
            EventLoopTaskCreationOptions = TaskCreationOptions.None,
            EventTaskCreationOptions = TaskCreationOptions.None,
            ReadChannelTaskOptions = TaskCreationOptions.LongRunning,
            WriteChannelTaskOptions = TaskCreationOptions.LongRunning,
            HandleMessagesOffEventLoop = true,
            NumberReaders = 1,
            KeepAliveInterval = null
        };

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate TConnectionState ConnectionStateProvider(Connection connection);

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate ValueTask HandleConnectionAsync(Connection connection);

        public delegate ValueTask<bool> HandleConnectionMessageAsync(Connection connection, TMessage message);

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate ValueTask HandleConnectionDisconnectionAsync(Connection connection, Exception ex, bool expected);

        public delegate ValueTask HandleConnectionKeepAliveAsync(Connection connection);

        private readonly ILogger<SocketServer<TConnectionState, TMessage>> logger;
        private readonly IMessageDeserializer<TMessage> deserializer;
        private readonly IMessageSerializer<TMessage> serializer;
        private readonly EventMessageStreamOptions clientMessageStreamOptions;
        private readonly RequestResponseKeyResolver<TMessage> rpcKeyResolver;
        private readonly ConnectionStateProvider connectionStateProvider;
        private readonly HandleConnectionAsync handleConnectionDelegate;
        private readonly HandleConnectionMessageAsync handleConnectionMessageDelegate;
        private readonly HandleConnectionDisconnectionAsync handleConnectionDisconnectionDelegate;
        private readonly HandleConnectionKeepAliveAsync handleConnectionKeepAliveDelegate;

        private Socket socketListener;

        private int connectionIdCounter = 0;

        private CancellationTokenSource acceptCts;
        private Task acceptTask;
        private SemaphoreSlim pendingConnectionLock;

        private ConcurrentDictionary<int, Connection> connections;

        public int ConnectionCount => connections.Count;

        public SocketServer(
            IMessageDeserializer<TMessage> deserializer,
            IMessageSerializer<TMessage> serializer,
            ConnectionStateProvider connectionStateProvider,
            HandleConnectionAsync handleConnectionDelegate,
            HandleConnectionMessageAsync handleMessageDelegate,
            HandleConnectionDisconnectionAsync handleConnectionDisconnectionDelegate,
            HandleConnectionKeepAliveAsync handleKeepAliveDelegate,
            EventMessageStreamOptions clientMessageStreamOptions = null,
            ILogger<SocketServer<TConnectionState, TMessage>> logger = null
        )
        {
            this.deserializer = deserializer;
            this.serializer = serializer;
            this.connectionStateProvider = connectionStateProvider;
            this.handleConnectionDelegate = handleConnectionDelegate;
            this.handleConnectionMessageDelegate = handleMessageDelegate;
            this.handleConnectionDisconnectionDelegate = handleConnectionDisconnectionDelegate;
            this.handleConnectionKeepAliveDelegate = handleKeepAliveDelegate;
            this.clientMessageStreamOptions = clientMessageStreamOptions ?? DefaultClientMessageStreamOptions;
            this.logger = logger;
        }

        public SocketServer(
            IMessageDeserializer<TMessage> deserializer,
            IMessageSerializer<TMessage> serializer,
            RequestResponseKeyResolver<TMessage> rpcKeyResolver,
            ConnectionStateProvider connectionStateProvider,
            HandleConnectionAsync handleConnectionDelegate,
            HandleConnectionMessageAsync handleMessageDelegate,
            HandleConnectionDisconnectionAsync handleConnectionDisconnectionDelegate,
            HandleConnectionKeepAliveAsync handleKeepAliveDelegate,
            EventMessageStreamOptions clientMessageStreamOptions = null,
            ILogger<SocketServer<TConnectionState, TMessage>> logger = null
        )
        {
            this.deserializer = deserializer;
            this.serializer = serializer;
            this.rpcKeyResolver = rpcKeyResolver;
            this.connectionStateProvider = connectionStateProvider;
            this.handleConnectionDelegate = handleConnectionDelegate;
            this.handleConnectionMessageDelegate = handleMessageDelegate;
            this.handleConnectionDisconnectionDelegate = handleConnectionDisconnectionDelegate;
            this.handleConnectionKeepAliveDelegate = handleKeepAliveDelegate;
            this.clientMessageStreamOptions = clientMessageStreamOptions ?? DefaultClientMessageStreamOptions;
            this.logger = logger;
        }

        public Task ListenAsync(int port, int tcpMaxPendingConnections = 1000, int maxPendingConnections = 50)
        {
            // TODO do we want to be able to listen on multiple ports? we should support that.
            logger?.LogInformation($"Starting socket server...");

            pendingConnectionLock = new SemaphoreSlim(maxPendingConnections);

            connections = new ConcurrentDictionary<int, Connection>();
            
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);

            // Create a TCP/IP socket.  
            socketListener = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            socketListener.Bind(localEndPoint);
            socketListener.Listen(tcpMaxPendingConnections);

            logger?.LogInformation($"Listening on {port} {{ tcpMaxPendingConnections={tcpMaxPendingConnections}, maxPendingConnections={maxPendingConnections} }}.");

            acceptCts = new CancellationTokenSource();

            // Pass the token to the wrapper task,
            // AcceptAsync doesnt support a cancellation token so we have to cancel the outer task.
            acceptTask = Task.Run(AcceptLoopAsync, acceptCts.Token); // TODO can you run this on multiple threads?

            logger?.LogDebug($"Accept loop started.");

            logger?.LogInformation($"Socket server started.");

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
                logger?.LogError(ex, "Error shutting down accept loop.");
            }

            socketListener.Close();
            connections.Clear();
            pendingConnectionLock.Dispose();

            logger?.LogInformation("Shutdown server.");
        }

        public bool TryGetConnection(int connectionId, out Connection connection)
        {
            return connections.TryGetValue(connectionId, out connection);
        }

        public Connection GetConnection(int connectionId)
        {
            return connections[connectionId];
        }

        public IEnumerable<Connection> GetConnections()
        {
            return connections.Values;
        }

        private ValueTask HandleKeepAliveAsync(Connection connection)
        {
            return handleConnectionKeepAliveDelegate(connection);
        }

        private async ValueTask HandleConnectionDisconnectAsync(Connection connection, Exception ex, bool expected)
        {
            try
            {
                logger?.LogInformation($"Closing connection {{ connectionId={connection.Id}, expected={expected}, reason={ex?.Message ?? "none"} }}.");

                await handleConnectionDisconnectionDelegate(connection, ex, expected).ConfigureAwait(false);

                logger?.LogInformation($"Closed connection {{ connectionId={connection.Id}, expected={expected}, reason={ex?.Message ?? "none"} }}.");
            }
            catch (Exception dcEx)
            {
                logger?.LogError(dcEx, $"Error disconnecting connection {{ connectionId={connection.Id} }}");
            }

            connections.TryRemove(connection.Id, out var con);
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
                    var connection = new Connection(connectionId, socket, this);

                    logger?.LogInformation($"Connection accepted {{ connectionId={connectionId} }}.");

                    // We have a socket, setup the message stream
                    var messageStream = new EventMessageStream<TMessage>(
                        deserializer,
                        serializer,
                        new SocketDuplexMessageStream(cancellationToken => new ValueTask<Socket>(socket)),
                        msg => HandleMessagAsync(connection, msg),
                        () => HandleKeepAliveAsync(connection),
                        (ex) => HandleConnectionDisconnectAsync(connection, ex, false),
                        clientMessageStreamOptions,
                        rpcKeyResolver
                    );

                    connection.State = connectionStateProvider(connection);
                    connection.MessageStream = messageStream;

                    if (!connections.TryAdd(connectionId, connection))
                    {
                        // TODO handle this. Shouldn't ever happen, but we should just disconnect the client.
                        var dcTask = connection.DisconnectAsync();
                        logger?.LogWarning($"Could not add connection to connection list. Disconnected connection. {{ connectionId={connectionId} }}.");
                        continue;
                    }

                    try
                    {
                        await messageStream.OpenAsync().ConfigureAwait(false);

                        logger?.LogDebug($"message-stream initialized. {{ connectionId={connectionId} }}");
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, $"Error initializing message-stream. {{ connectionId={connectionId} }}.");
                        var dcTask = connection.DisconnectAsync();
                        connections.TryRemove(connectionId, out var con);
                        continue;
                    }

                    var _ = Task.Run(async () =>
                    {
                        await pendingConnectionLock.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            await handleConnectionDelegate(connection).ConfigureAwait(false);

                            logger?.LogDebug($"Connection initialized {{ connectionId={connectionId} }}.");
                        } 
                        catch (Exception ex)
                        {
                            logger?.LogError(ex, $"Error initializing connection. Disconnecting. {{ connectionId={connectionId} }}");
                            var dcTask = connection.DisconnectAsync();
                            connections.TryRemove(connectionId, out var con);
                        }
                        finally
                        {
                            pendingConnectionLock.Release();
                        }
                    });

                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Exception in accept loop.");

                    bool close = await HandleAcceptExceptionAsync(ex).ConfigureAwait(false);
                    if (close)
                    {
                        logger?.LogInformation("Closing accept loop because of exception.");
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

            public Connection(int id, Socket socket, SocketServer<TConnectionState, TMessage> socketServer)
            {
                Id = id;
                Socket = socket;
                SocketServer = socketServer;
            }

            public int Id { get; }

            public TConnectionState State { get; internal set; }

            public bool GracefulDisconnect { get; private set; }

            internal Socket Socket { get; }

            internal SocketServer<TConnectionState, TMessage> SocketServer { get; }

            public EventMessageStream<TMessage> MessageStream { get; set; }

            public async Task DisconnectAsync()
            {
                GracefulDisconnect = true;

                try
                {
                    await MessageStream.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SocketServer.logger?.LogError(ex, "Error closing connection message stream.");
                }

                await SocketServer.HandleConnectionDisconnectAsync(this, null, true).ConfigureAwait(false);
            }

            /// <summary>
            /// NOTE: All writes will be reported as success because messages are dropped
            /// into a queue that are written later. If you would like to wait for an actual result on the write,
            /// use WriteAndWaitAsync
            /// </summary>
            public ValueTask<MessageWriteResult> WriteAsync(TMessage message, bool flush = true)
            {
                return MessageStream.WriteAsync(message, flush);
            }

            /// <summary>
            /// Writes the message and waits until it's actually been written to the pipe. Slower than WriteAsync
            /// </summary>
            public ValueTask<MessageWriteResult> ConfirmWriteAsync(TMessage message, bool flush = true)
            {
                return MessageStream.ConfirmWriteAsync(message, flush);
            }

            /// <summary>
            /// Writes a message and waits for a specific message to come back.
            /// </summary>
            public Task<MessageWriteRequestResult<TReply>> WriteRequestAsync<TReply>(TMessage message, TimeSpan timeout = default, bool flush = true) where TReply : TMessage
            {
                return MessageStream.WriteRequestAsync<TReply>(message, timeout, flush);
            }

        }

    }
}
