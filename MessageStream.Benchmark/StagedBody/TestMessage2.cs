using MessageStream.Message;
using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Benchmark.StagedBody
{

    public class TestMessage2 : IStagedBodyMessage
    {

        public const int Id = 2;

        public StagedBodyMessageHeader Header { get; set; }

        public short MessageId => Id;

        public uint Value { get; set; }

    }

    public class TestMessage2Deserializer : StagedBodyMessageDeserializer.IStagedBodyMessageBodyDeserializer<TestMessage2>
    {

        public int Identifier => TestMessage2.Id;

        public void DeserializeOnto(in ReadOnlySpan<byte> buffer, StagedBodyMessageHeader state, ref TestMessage2 message)
        {
            int index = 0;
            message.Value = buffer.ReadUInt(ref index);
        }

    }

    public class TestMessage2Serializer : StagedBodyMessageSerializer.UnknownSizeMessageBodySerializer<TestMessage2>
    {

        protected override Span<byte> Serialize(TestMessage2 message)
        {
            Span<byte> buffer = new Span<byte>(new byte[4]);
            int offset = 0;
            buffer.WriteUInt(ref offset, message.Value);
            return buffer;
        }

    }

}
