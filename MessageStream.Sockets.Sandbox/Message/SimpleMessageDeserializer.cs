using MessageStream.Message;
using System;

namespace MessageStream.Sockets.Sandbox
{
    public class SimpleMessageDeserializer : FixedSizeMessageDeserializer<SimpleMessage>
    {

        protected override int MessageSize => 4;

        public PooledMessageProvider<int, SimpleMessage> messageProvider = new PooledMessageProvider<int, SimpleMessage>();

        protected override SimpleMessage Deserialize(in ReadOnlySpan<byte> buffer)
        {
            int index = 0;
            var msg = messageProvider.GetMessage<SimpleMessage>(0);

            msg.Id = buffer.ReadShort(ref index);
            msg.Value = buffer.ReadShort(ref index);

            return msg;
        }

    }
}