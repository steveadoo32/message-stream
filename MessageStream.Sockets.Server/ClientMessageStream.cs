using MessageStream.Message;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Server
{
    public class ClientMessageStream<T> : EventMessageStream<T>
    {

        private readonly ClientSocketReaderWriter socketReaderWriter;

        public ClientMessageStream(
            ClientSocketReaderWriter socketReaderWriter,
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
            RequestResponseKeyResolver<T> rpcKeyResolver,
            HandleMessageAsync handleMessageDelegate,
            HandleDisconnectionAsync handleDisconnectionDelegate,
            HandleKeepAliveAsync handleKeepAliveDelegate,
            int numReaders = 1,
            bool handleMessagesAsynchronously = false,
            TimeSpan? keepAliveTimeSpan = null,
            PipeOptions readerPipeOptions = null,
            PipeOptions writerPipeOptions = null,
            TimeSpan? writerCloseTimeout = null,
            ChannelOptions readerChannelOptions = null,
            ChannelOptions writerChannelOptions = null,
            TimeSpan? readerFlushTimeout = null
        ) : base(socketReaderWriter, deserializer, socketReaderWriter, serializer, rpcKeyResolver, handleMessageDelegate, handleDisconnectionDelegate, handleKeepAliveDelegate, numReaders, handleMessagesAsynchronously, keepAliveTimeSpan, readerPipeOptions, writerPipeOptions, writerCloseTimeout, readerChannelOptions, writerChannelOptions, readerFlushTimeout)
        {
            this.socketReaderWriter = socketReaderWriter;
        }

        public ClientMessageStream(
            Socket socket,
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
            RequestResponseKeyResolver<T> rpcKeyResolver,
            HandleMessageAsync handleMessageDelegate,
            HandleDisconnectionAsync handleDisconnectionDelegate,
            HandleKeepAliveAsync handleKeepAliveDelegate,
            int numReaders = 1,
            bool handleMessagesAsynchronously = false,
            TimeSpan? keepAliveTimeSpan = null,
            PipeOptions readerPipeOptions = null,
            PipeOptions writerPipeOptions = null,
            TimeSpan? writerCloseTimeout = null,
            ChannelOptions readerChannelOptions = null,
            ChannelOptions writerChannelOptions = null,
            TimeSpan? readerFlushTimeout = null
        ) : this(new ClientSocketReaderWriter(socket), deserializer, serializer, rpcKeyResolver, handleMessageDelegate, handleDisconnectionDelegate, handleKeepAliveDelegate, numReaders, handleMessagesAsynchronously, keepAliveTimeSpan, readerPipeOptions, writerPipeOptions, writerCloseTimeout, readerChannelOptions, writerChannelOptions, readerFlushTimeout)
        {
        }

        protected override async Task CleanupAsync()
        {
            await socketReaderWriter.DisconnectAsync().ConfigureAwait(false);
        }

    }
}
