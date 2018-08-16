using MessageStream.Message;
using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Benchmark.StagedBody
{

    public class TestMessage : IStagedBodyMessage
    {

        public const int Id = 1;

        public StagedBodyMessageHeader Header { get; set; }

        public short MessageId => Id;

        public short Value { get; set; }
        
    }

    public class TestMessageDeserializer : StagedBodyMessageDeserializer.IStagedBodyMessageBodyDeserializer<TestMessage>
    {

        public int Identifier => TestMessage.Id;

        public void DeserializeOnto(in ReadOnlySpan<byte> buffer, StagedBodyMessageHeader state, ref TestMessage message)
        {
            int index = 0;
            message.Value = buffer.ReadShort(ref index);
        }

    }

    public class TestMessageSerializer : StagedBodyMessageSerializer.KnownSizeMessageBodySerializer<TestMessage>
    {

        protected override int GetMessageSize(TestMessage message) => 2;

        protected override void Serialize(TestMessage message, in Span<byte> buffer)
        {
            int offset = 0;
            buffer.WriteShort(ref offset, message.Value);
        }

    }

}
