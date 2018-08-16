using MessageStream.Serializer;
using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Tests.StagedBody
{

    public class TestMessage : IStagedBodyMessage
    {

        public const int MessageId = 1;

        public StagedBodyMessageHeader Header { get; set; }

        public short Value { get; set; }

    }

    public class TestMessageDeserializer : StagedBodyMessageDeserializer.IStagedBodyMessageBodyDeserializer<TestMessage>
    {

        public int Identifier => TestMessage.MessageId;

        public void DeserializeOnto(in ReadOnlySpan<byte> buffer, StagedBodyMessageHeader state, ref TestMessage message)
        {
            int index = 0;
            message.Value = buffer.ReadShort(ref index);
        }

    }
    
}
