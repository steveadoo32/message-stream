using MessageStream.Message;
using System;

namespace MessageStream.Sockets.Sandbox
{
    public class SimpleMessageDeserializer : FixedSizeMessageDeserializer<SimpleMessage>
    {

        protected override int MessageSize => 10;

        public PooledMessageProvider<int, SimpleMessage> MessageProvider { get; }

        public SimpleMessageDeserializer(bool enablePool = true)
        {
            if (enablePool)
                MessageProvider = new PooledMessageProvider<int, SimpleMessage>();
        }

        protected override SimpleMessage Deserialize(in ReadOnlySpan<byte> buffer)
        {
            int index = 0;
            var msg = MessageProvider?.GetMessage<SimpleMessage>(0) ?? new SimpleMessage();

            msg.Id = buffer.ReadInt(ref index);
            msg.Value = buffer.ReadInt(ref index);
            msg.DontReply = buffer.ReadByte(ref index) == 1;
            msg.Disconnect = buffer.ReadByte(ref index) == 1;

            return msg;
        }

    }
}