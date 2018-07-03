using System;
using System.Buffers;
using MessageStream.Serializer;

namespace MessageStream.Tests.StagedBody
{
    public class StagedBodyMessageDeserializer : StagedDeserializer<IStagedBodyMessage>
    {

        private readonly MessageBodyDeserializer<int, IStagedBodyMessage, StagedBodyMessageHeader> messageBodyDeserializer;

        public override int HeaderLength => 4;

        public StagedBodyMessageDeserializer(
            IMessageProvider<int, IStagedBodyMessage> messageProvider,
            params IMessageBodyDeserializer<int>[] deserializers
        )
        {
            this.messageBodyDeserializer = new MessageBodyDeserializer<int, IStagedBodyMessage, StagedBodyMessageHeader>(
                    messageProvider,
                    deserializers
                );
        }

        [DeserializationStage(0)]
        public StagedBodyMessageHeader Deserialize(in ReadOnlySpan<byte> buffer, out int nextStageLength)
        {
            int index = 0;

            short messageId = buffer.ReadShort(ref index);
            short messageBodyLength = buffer.ReadShort(ref index);

            nextStageLength = messageBodyLength;

            return new StagedBodyMessageHeader
            {
                MessageId = messageId,
                MessageBodyLength = messageBodyLength
            };
        }

        [DeserializationStage(1)]
        public IStagedBodyMessage Deserialize(in ReadOnlySpan<byte> buffer, StagedBodyMessageHeader header)
        {
            var message = messageBodyDeserializer.Deserialize(buffer, header, header.MessageId);

            message.Header = header;

            return message;
        }

    }

    public interface IStagedBodyMessageBodyDeserializer<TMessage> : IMessageBodyDeserializer<int, StagedBodyMessageHeader, TMessage>
    {

    }

}