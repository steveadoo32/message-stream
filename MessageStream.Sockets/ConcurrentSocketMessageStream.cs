using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using MessageStream.IO;
using MessageStream.Message;

namespace MessageStream.Sockets
{
    public class ConcurrentSocketMessageStream<T> : ConcurrentMessageStream<T>
    {

        private readonly SocketReaderWriter socketReaderWriter;

        public SocketConfiguration SocketConfiguration { get; }

        private ConcurrentSocketMessageStream(
            SocketConfiguration socketConfiguration,
            SocketReaderWriter socketReaderWriter,
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
            PipeOptions readerPipeOptions = null,
            PipeOptions writerPipeOptions = null,
            TimeSpan? writerCloseTimeout = null
        ) : base(socketReaderWriter, deserializer, socketReaderWriter, serializer, readerPipeOptions, writerPipeOptions, writerCloseTimeout)
        {
            this.socketReaderWriter = socketReaderWriter;
            SocketConfiguration = socketConfiguration;
        }

        public ConcurrentSocketMessageStream(
            SocketConfiguration socketConfiguration,
            IMessageDeserializer<T> deserializer, 
            IMessageSerializer<T> serializer, 
            PipeOptions readerPipeOptions = null, 
            PipeOptions writerPipeOptions = null, 
            TimeSpan? writerCloseTimeout = null
        ) : this(socketConfiguration, new SocketReaderWriter(), deserializer, serializer, readerPipeOptions, writerPipeOptions, writerCloseTimeout)
        {
        }

        public override async Task OpenAsync()
        {
            await socketReaderWriter.ConnectAsync(SocketConfiguration).ConfigureAwait(false);

            await base.OpenAsync().ConfigureAwait(false);
        }

        protected override async Task CleanupAsync()
        {
            await socketReaderWriter.DisconnectAsync().ConfigureAwait(false);
        }

    }
}
