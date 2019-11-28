using MessageStream.Message;
using System;
using System.Buffers;

namespace MessageStream.Sockets.Sandbox
{
    public class SimpleMessageDeserializer : IMessageDeserializer<SimpleMessage>
    {

        public PooledMessageProvider<int, SimpleMessage> MessageProvider { get; }

        public SimpleMessageDeserializer(bool enablePool = true)
        {
            if (enablePool)
                MessageProvider = new PooledMessageProvider<int, SimpleMessage>();
        }

        unsafe bool IMessageDeserializer<SimpleMessage>.Deserialize(in ReadOnlySequence<byte> buffer, out SequencePosition read, out SimpleMessage message)
        {
            read = buffer.Start;
            message = default;

            // Make sure we have enough data
            if (buffer.Length - 12 < 0)
            {
                return false;
            }

            var bufferReader = new BufferReader(buffer);

            byte* messageBufferPtr = stackalloc byte[12];
            Span<byte> messageBuffer = new Span<byte>(messageBufferPtr, 12);

            // Copying to a local buffer is faster than reading from BufferReader one at a time,
            // So copy the whole buffer into an array on the stack.
            var localRead = read;
            bufferReader.ReadBytes(ref localRead, messageBuffer);
            
            // Create a ReadOnlySpan around the memory so implementations know they shouldn't modify the memory
            var messageBufferSpan = new ReadOnlySpan<byte>(messageBufferPtr, 12);

            int index = 10;
            var dummyLen = messageBufferSpan.ReadShort(ref index);

            if (buffer.Length - 12 - dummyLen < 0)
            {
                return false;
            }

            int totalSize = 12 + (int)dummyLen;
            byte* messageBufferPtr2 = stackalloc byte[totalSize];
            var messageBuffer2 = new Span<byte>(messageBufferPtr2, totalSize);
            messageBuffer.CopyTo(messageBuffer2);
            bufferReader.ReadBytes(ref read, messageBuffer2.Slice(12));

            messageBufferSpan = new ReadOnlySpan<byte>(messageBufferPtr2, totalSize);

            // Read the body buffer into a message
            message = Deserialize(messageBufferSpan);

            return true;
        }

        protected SimpleMessage Deserialize(in ReadOnlySpan<byte> buffer)
        {
            int index = 0;
            var msg = MessageProvider?.GetMessage<SimpleMessage>(0) ?? new SimpleMessage();

            msg.Id = buffer.ReadInt(ref index);
            msg.Value = buffer.ReadInt(ref index);
            msg.DontReply = buffer.ReadByte(ref index) == 1;
            msg.Disconnect = buffer.ReadByte(ref index) == 1;
            msg.Dummy = new byte[buffer.ReadShort(ref index)];
            for(int i = 0; i < msg.Dummy.Length; i++)
            {
                msg.Dummy[i] = buffer.ReadByte(ref index);
            }

            return msg;
        }

    }
}