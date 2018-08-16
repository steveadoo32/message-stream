using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Message
{


    /// <summary>
    /// A fixed size message deserializer
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class FixedSizeMessageDeserializer<T> : IMessageDeserializer<T>
    {

        /// <summary>
        /// Size of the messages
        /// </summary>
        protected abstract int MessageSize { get; }

        unsafe bool IMessageDeserializer<T>.Deserialize(in ReadOnlySequence<byte> buffer, out SequencePosition read, out T message)
        {
            read = buffer.Start;
            message = default;
            
            // Make sure we have enough data
            if (buffer.Length - MessageSize < 0)
            {
                return false;
            }

            var bufferReader = new BufferReader(buffer);

            byte* messageBufferPtr = stackalloc byte[MessageSize];
            Span<byte> messageBuffer = new Span<byte>(messageBufferPtr, MessageSize);

            // Copying to a local buffer is faster than reading from BufferReader one at a time,
            // So copy the whole buffer into an array on the stack.
            bufferReader.ReadBytes(ref read, messageBuffer);

            // Mark the position on the buffer
            read = bufferReader.Position;

            // Create a ReadOnlySpan around the memory so implementations know they shouldn't modify the memory
            var messageBufferSpan = new ReadOnlySpan<byte>(messageBufferPtr, MessageSize);

            // Read the body buffer into a message
            message = Deserialize(messageBufferSpan);

            return true;
        }

        protected abstract T Deserialize(in ReadOnlySpan<byte> buffer);

    }
}
