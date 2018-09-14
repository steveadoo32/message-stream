using System;
using MessageStream.Message;

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
        
        int IMessageBodyDeserializer<int>.Identifier => TestMessage.Id;

        void IMessageBodyDeserializer<int, StagedBodyMessageHeader, TestMessage>.DeserializeOnto(in ReadOnlySpan<byte> buffer, StagedBodyMessageHeader state, ref TestMessage message)
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
