using System;
using System.Buffers;
using MessageStream.Serializer;

namespace MessageStream.Tests.StagedBody
{
    public class StagedBodyMessageDeserializer : MessageWithHeaderDeserializer<StagedBodyMessageHeader, int, IStagedBodyMessage>
    {
        
        public override int HeaderLength => 4;

        public StagedBodyMessageDeserializer(
            IMessageProvider<int, IStagedBodyMessage> messageProvider,
            params IMessageBodyDeserializer<int>[] deserializers
        )
            : base(messageProvider, deserializers)
        {
        }

        protected override int GetMessageIdentifier(StagedBodyMessageHeader header) => header.MessageBodyLength;

        protected override StagedBodyMessageHeader ReadHeader(in ReadOnlySpan<byte> headerBuffer, out int bodyLength)
        {
            int index = 0;

            short messageId = headerBuffer.ReadShort(ref index);
            short messageBodyLength = headerBuffer.ReadShort(ref index);

            bodyLength = messageBodyLength;

            return new StagedBodyMessageHeader
            {
                MessageId = messageId,
                MessageBodyLength = messageBodyLength
            };
        }

        protected override void ProcessMessage(StagedBodyMessageHeader header, IStagedBodyMessage message)
        {
            message.Header = header;
        }

    }
    
}