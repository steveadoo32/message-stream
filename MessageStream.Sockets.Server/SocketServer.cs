using MessageStream.Message;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Server
{
    public class SocketServer<T>
    {
        
        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate ValueTask HandleConnectionAsync(Connection connection);

        public delegate ValueTask<bool> HandleConnectionMessageAsync(Connection connection, T message);

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream for you.
        /// </summary>
        public delegate ValueTask HandleConnectionDisconnectionAsync(Connection connection, Exception ex, bool expected);

        public delegate ValueTask HandleConnectionKeepAliveAsync(Connection connection);

        private readonly HandleConnectionAsync handleConnectionDelegate;
        private readonly HandleConnectionMessageAsync handleConnectionMessageDelegate;
        private readonly HandleConnectionDisconnectionAsync handleConnectionDisconnectionDelegate;
        private readonly HandleConnectionKeepAliveAsync handleConnectionKeepAliveDelegate;

        private readonly IMessageDeserializer<T> deserializer;
        private readonly IMessageSerializer<T> serializer;

        private Socket socketListener;

        private int clientIdCounter = 0;

        private CancellationTokenSource acceptCts;
        private Task acceptTask;

        private ConcurrentDictionary<int, Connection> Connections { get; set; }

        public SocketServer(
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
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

            Connections = new ConcurrentDictionary<int, Connection>();
            
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);

            // Create a TCP/IP socket.  
            socketListener = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            socketListener.Bind(localEndPoint);
            socketListener.Listen(maxPendingConnections);

            acceptCts = new CancellationTokenSource();

            // Pass the token to the wrapper task,
            // AcceptAsync doesnt support a cancellation token so we have to cancel the outer task.
            acceptTask = Task.Run(AcceptLoopAsync, acceptCts.Token); // TODO can you run this on multiple threads?

            // Anything else to do?

            return Task.CompletedTask;
        }

        public async Task CloseAsync()
        {

        }

        private ValueTask HandleKeepAliveAsync(Connection connection)
        {
            return handleConnectionKeepAliveDelegate(connection);
        }

        private ValueTask HandleConnectionDisconnectAsync(Connection connection, Exception ex, bool expected)
        {
            return handleConnectionDisconnectionDelegate(connection, ex, expected);
        }

        private ValueTask<bool> HandleMessagAsync(Connection connection, T msg)
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

                    int clientId = Interlocked.Increment(ref clientIdCounter);
                    var connection = new Connection(clientId, socket);

                    // We have a socket, setup the message stream
                    var messageStream = new ClientMessageStream<T>(
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

                    if (!Connections.TryAdd(clientId, connection))
                    {
                        // TODO handle this. Shouldn't ever happen, but we should just disconnect the client.
                    }

                    await messageStream.OpenAsync().ConfigureAwait(false);

                    await handleConnectionDelegate(connection).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    bool close = await HandleAcceptExceptionAsync(ex).ConfigureAwait(false);
                    if (close)
                    {
                        break;
                    }
                }
            }
        }

        private async Task<bool> HandleAcceptExceptionAsync(Exception ex)
        {
            return false;
        }

        #endregion

        public class Connection
        {

            private Socket socket;

            public Connection(int id, Socket socket)
            {
                Id = id;
                Socket = socket;
            }

            public int Id { get; }

            internal Socket Socket { get; }

            internal ClientMessageStream<T> MessageStream { get; set; }

            /// <summary>
            /// NOTE: All writes will be reported as success because messages are dropped
            /// into a queue that are written later. If you would like to wait for an actual result on the write,
            /// use WriteAndWaitAsync
            /// </summary>
            public ValueTask<MessageWriteResult> WriteAsync(T message)
            {
                return MessageStream.WriteAsync(message);
            }

            /// <summary>
            /// Writes the message and waits until it's actually been written to the pipe. Slower than WriteAsync
            /// </summary>
            public ValueTask<MessageWriteResult> WriteAndWaitAsync(T message)
            {
                return MessageStream.WriteAndWaitAsync(message);
            }

            /// <summary>
            /// Writes a message and waits for a specific message to come back.
            /// </summary>
            public ValueTask<MessageWriteRequestResult<TReply>> WriteRequestAsync<TReply>(T message, Func<T, bool> matchFunc = null, int timeoutMilliseconds = -1) where TReply : T
            {
                return MessageStream.WriteRequestAsync<TReply>(message, matchFunc, timeoutMilliseconds);
            }

        }

    }
}
