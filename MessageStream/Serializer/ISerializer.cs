using System;

namespace MessageStream.Serializer
{
    public interface ISerializer<T>
    {

        /// <summary>
        /// If this serializer knows message sizes before hand, use this to give yourself a fixed buffer in <see cref="Serialize(T, in Span{byte}, bool)"/>.
        /// </summary>
        /// <param name="size">The size in bytes that this serialized message will be</param>
        /// <returns>If this serializer could determine the size or not.</returns>
        bool TryCalculateMessageSize(T message, out int size);

        /// <summary>
        /// Serializes the message
        /// </summary>
        /// <param name="buffer">The buffer to serialize into IF TryCalculateMessageSize returned true.</param>
        /// <param name="bufferProvided">If buffer is provided, you can write into that and return it.</param>
        /// <returns>The serialized message</returns>
        Span<byte> Serialize(T message, in Span<byte> buffer = default, bool bufferProvided = false);

    }
}