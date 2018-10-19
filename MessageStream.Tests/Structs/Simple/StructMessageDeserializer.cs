using MessageStream.Message;
using System;

namespace MessageStream.Tests.Structs.Simple
{
    internal class StructMessageDeserializer : FixedSizeMessageDeserializer<StructMessage>
    {

        protected override int MessageSize => 4;

        protected override StructMessage Deserialize(in ReadOnlySpan<byte> buffer)
        {
            int index = 0;
            return new StructMessage
            {
                Id = buffer.ReadShort(ref index),
                Value = buffer.ReadShort(ref index)
            };
        }

    }
}