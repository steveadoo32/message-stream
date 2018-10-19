using MessageStream.Message;
using System;

namespace MessageStream.Sockets.Sandbox
{
    public class SimpleMessageDeserializer : FixedSizeMessageDeserializer<SimpleMessage>
    {

        protected override int MessageSize => 4;

        protected override SimpleMessage Deserialize(in ReadOnlySpan<byte> buffer)
        {
            int index = 0;
            return new SimpleMessage
            {
                Id = buffer.ReadShort(ref index),
                Value = buffer.ReadShort(ref index)
            };
        }

    }
}