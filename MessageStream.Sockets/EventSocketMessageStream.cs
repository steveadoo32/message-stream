using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessageStream.IO;
using MessageStream.Message;

namespace MessageStream.Sockets
{
    public class EventSocketMessageStream<T> : EventMessageStream<T>
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly SocketReaderWriter socketReaderWriter;

        public SocketConfiguration SocketConfiguration { get; }

        private EventSocketMessageStream(
            SocketConfiguration socketConfiguration,
            SocketReaderWriter socketReaderWriter,
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
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
        ) : base(socketReaderWriter, deserializer, socketReaderWriter, serializer, handleMessageDelegate, handleDisconnectionDelegate, handleKeepAliveDelegate, numReaders, handleMessagesAsynchronously, keepAliveTimeSpan, readerPipeOptions, writerPipeOptions, writerCloseTimeout, readerChannelOptions, writerChannelOptions, readerFlushTimeout)
        {
            this.socketReaderWriter = socketReaderWriter;
            SocketConfiguration = socketConfiguration;
        }

        public EventSocketMessageStream(
            SocketConfiguration socketConfiguration,
            IMessageDeserializer<T> deserializer, 
            IMessageSerializer<T> serializer,
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
        ) : this(socketConfiguration, new SocketReaderWriter(), deserializer, serializer, handleMessageDelegate, handleDisconnectionDelegate, handleKeepAliveDelegate, numReaders, handleMessagesAsynchronously, keepAliveTimeSpan, readerPipeOptions, writerPipeOptions, writerCloseTimeout, readerChannelOptions, writerChannelOptions, readerFlushTimeout)
        {
        }

        public override async Task OpenAsync()
        {
            await socketReaderWriter.ConnectAsync(SocketConfiguration).ConfigureAwait(false);

            await base.OpenAsync().ConfigureAwait(false);
        }

        protected override async Task CleanupAsync()
        {
            try
            {
                // This could throw an exception, we aren't sure so we'll catch it.
                await socketReaderWriter.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error disconnecting socket.");
            }
        }

        // The sockets dont cancel their read when we cancel the token so this forces the read operation to stop.
        protected override async Task InnerCloseAsync()
        {
            try
            {
                // This could throw an exception, we aren't sure so we'll catch it.
                await socketReaderWriter.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error disconnecting socket.");
            }

            await base.InnerCloseAsync().ConfigureAwait(false);
        }

    }
}
