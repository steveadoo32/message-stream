using System;
using System.Buffers;

namespace MessageStream.Message
{
    public interface IMessageDeserializer<T>
    {

        /// <summary>
        /// Deserializes the buffer into a message
        /// </summary>
        /// <param name="buffer">The buffer to read from</param>
        /// <param name="read">The position we stopped reading at for this message.</param>
        /// <param name="message">The read message</param>
        /// <returns>If we had enough data and deserialized the message</returns>
        bool Deserialize(in ReadOnlySequence<byte> buffer, out SequencePosition read, out T message);

    }
}