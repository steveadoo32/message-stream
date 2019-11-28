using MessageStream.Message;
using System;

namespace MessageStream.Sockets.Sandbox
{
    public class SimpleMessageSerializer : IMessageSerializer<SimpleMessage>
    {
        public Span<byte> Serialize(SimpleMessage message, in Span<byte> buffer, bool bufferProvided)
        {
            int index = 0;

            buffer.WriteInt(ref index, message.Id);
            buffer.WriteInt(ref index, message.Value);
            buffer.WriteByte(ref index, message.DontReply ? (byte) 1 : (byte) 0);
            buffer.WriteByte(ref index, message.Disconnect ? (byte) 1 : (byte) 0);
            buffer.WriteShort(ref index, (short) (message.Dummy?.Length ?? 0));

            for(int i = 0; i < (message.Dummy?.Length?? 0); i++)
            {
                buffer.WriteByte(ref index, message.Dummy[i]);
            }

            return buffer;
        }

        public bool TryCalculateMessageSize(SimpleMessage message, out int size)
        {
            size = 12 + (message.Dummy?.Length ?? 0);
            return true;
        }
    }
}