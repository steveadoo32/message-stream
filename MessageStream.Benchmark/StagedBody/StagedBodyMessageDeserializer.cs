using System;
using System.Buffers;
using MessageStream.Message;

namespace MessageStream.Benchmark.StagedBody
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

        protected override int GetMessageIdentifier(StagedBodyMessageHeader header) => header.MessageId;

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
        
    }
    
}