using MessageStream.Message;
using System;

namespace MessageStream.Tests.Simple
{
    internal class SimpleMessageSerializer : IMessageSerializer<SimpleMessage>
    {
        public Span<byte> Serialize(SimpleMessage message, in Span<byte> buffer, bool bufferProvided)
        {
            int index = 0;

            buffer.WriteShort(ref index, message.Id);
            buffer.WriteShort(ref index, message.Value);

            return buffer;
        }

        public bool TryCalculateMessageSize(SimpleMessage message, out int size)
        {
            size = 4;
            return true;
        }
    }
}