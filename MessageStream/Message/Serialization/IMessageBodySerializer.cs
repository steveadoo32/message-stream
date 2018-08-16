using System;

namespace MessageStream.Message
{
    public interface IMessageBodySerializer<TIdentifier, T>
    {

        TIdentifier Identifier { get; }

        bool TryCalculateMessageSize(T message, out int size);

        Span<byte> Serialize(T message, in Span<byte> buffer, bool bufferProvided);

    }
}