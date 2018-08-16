using MessageStream.Message;
using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Benchmark.StagedBody
{

    public class StagedBodyMessageSerializer : MessageWithHeaderSerializer<IStagedBodyMessage>
    {

        protected override int HeaderLength => 4;

        public StagedBodyMessageSerializer(
            params IMessageBodySerializer<Type, IStagedBodyMessage>[] serializers
        )
            : base(serializers)
        {
        }

        protected override void SerializeHeader(in Span<byte> buffer, IStagedBodyMessage message, int bodySize)
        {
            int offset = 0;

            buffer.WriteShort(ref offset, message.MessageId);
            buffer.WriteShort(ref offset, (short) bodySize);
        }

    }

}
