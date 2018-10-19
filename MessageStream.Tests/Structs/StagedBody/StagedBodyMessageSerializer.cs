using MessageStream.Message;
using System;

namespace MessageStream.Tests.Structs.StagedBody
{

    public class StagedBodyMessageSerializer : MessageWithHeaderSerializer<TestMessage>
    {

        protected override int HeaderLength => 4;

        public StagedBodyMessageSerializer(
            params IMessageBodySerializer<Type, TestMessage>[] serializers
        )
            : base(serializers)
        {
        }

        protected override void SerializeHeader(in Span<byte> buffer, TestMessage message, int bodySize)
        {
            int offset = 0;

            buffer.WriteShort(ref offset, message.MessageId);
            buffer.WriteShort(ref offset, (short) bodySize);
        }

    }

}
