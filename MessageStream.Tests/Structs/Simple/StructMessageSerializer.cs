using MessageStream.Message;
using System;

namespace MessageStream.Tests.Structs.Simple
{
    internal class StructMessageSerializer : IMessageSerializer<StructMessage>
    {
        public Span<byte> Serialize(StructMessage message, in Span<byte> buffer, bool bufferProvided)
        {
            int index = 0;

            buffer.WriteShort(ref index, message.Id);
            buffer.WriteShort(ref index, message.Value);

            return buffer;
        }

        public bool TryCalculateMessageSize(StructMessage message, out int size)
        {
            size = 4;
            return true;
        }
    }
}