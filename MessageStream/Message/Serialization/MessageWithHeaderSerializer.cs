using System;
using System.Collections.Generic;
using System.Linq;

namespace MessageStream.Message
{

    public abstract class MessageWithHeaderSerializer<T> : MessageWithHeaderSerializer<Type, T>
    {

        protected override Type GetIdentifier(T message) => message.GetType();

        public MessageWithHeaderSerializer(
            params IMessageBodySerializer<Type, T>[] serializers
        )
            : base(serializers)
        {
        }

        public new abstract class MessageBodySerializer<TMessage> : MessageWithHeaderSerializer<Type, T>.MessageBodySerializer<TMessage> where TMessage : T
        {

            public override Type Identifier => typeof(TMessage);
            
        }

        public new abstract class UnknownSizeMessageBodySerializer<TMessage> : MessageWithHeaderSerializer<Type, T>.UnknownSizeMessageBodySerializer<TMessage> where TMessage : T
        {

            public override Type Identifier => typeof(TMessage);

        }

        public new abstract class KnownSizeMessageBodySerializer<TMessage> : MessageWithHeaderSerializer<Type, T>.KnownSizeMessageBodySerializer<TMessage> where TMessage : T
        {

            public override Type Identifier => typeof(TMessage);

        }

    }

    public abstract class MessageWithHeaderSerializer<TIdentifier, T> : IMessageSerializer<T>
    {

        private readonly Dictionary<TIdentifier, IMessageBodySerializer<TIdentifier, T>> serializers;

        protected abstract int HeaderLength { get; }

        public MessageWithHeaderSerializer(
            params IMessageBodySerializer<TIdentifier, T>[] serializers
        )
        {
            this.serializers = serializers.ToDictionary(serializer => serializer.Identifier);
        }

        public bool TryCalculateMessageSize(T message, out int size)
        {
            var serializer = GetSerializer(message);

            bool calculated = serializer.TryCalculateMessageSize(message, out size);

            // If we did calculate the message body size, just add the header size to it.
            // If we couldn't, Serialize will handle creating a buffer for the header and one for the body and combining them.
            if (calculated)
            {
                size += HeaderLength;
            }

            return calculated;
        }

        public Span<byte> Serialize(T message, in Span<byte> buffer, bool bufferProvided)
        {
            if (bufferProvided)
            {
                return HandleKnownBufferSize(in buffer, message);
            }
            else
            {
                return HandleUnknownBufferSize(message);
            }
        }

        private Span<byte> HandleKnownBufferSize(in Span<byte> buffer, T message)
        {
            var serializer = GetSerializer(message);

            var headerBuffer = buffer.Slice(0, HeaderLength);
            var bodyBuffer = buffer.Slice(HeaderLength);

            SerializeHeader(in headerBuffer, message, buffer.Length - HeaderLength);
            // Ignore return value here.
            serializer.Serialize(message, in bodyBuffer, true);

            return buffer;
        }

        private unsafe Span<byte> HandleUnknownBufferSize(T message)
        {
            var serializer = GetSerializer(message);

            // Serialize the body first so we can pass the correct size into SerializeHeader
            var bodyBuffer = serializer.Serialize(message, default, false);
            var combinedBuffer = new Span<byte>(new byte[HeaderLength + bodyBuffer.Length]);

            bodyBuffer.CopyTo(combinedBuffer.Slice(HeaderLength));

            byte* headerBufferPtr = stackalloc byte[HeaderLength];
            var headerBuffer = new Span<byte>(headerBufferPtr, HeaderLength);
            SerializeHeader(in headerBuffer, message, bodyBuffer.Length);

            headerBuffer.CopyTo(combinedBuffer.Slice(0, HeaderLength));

            return combinedBuffer;
        }

        private IMessageBodySerializer<TIdentifier, T> GetSerializer(T message)
        {
            var identifier = GetIdentifier(message);
            return serializers[identifier];
        }

        protected abstract TIdentifier GetIdentifier(T message);

        protected abstract void SerializeHeader(in Span<byte> buffer, T message, int bodySize);
        
        public abstract class MessageBodySerializer<TMessage> : IMessageBodySerializer<TIdentifier, T> where TMessage : T
        {

            public abstract TIdentifier Identifier { get; }

            public bool TryCalculateMessageSize(T message, out int size)
            {
                return TryCalculateMessageSize((TMessage)message, out size);
            }

            public  Span<byte> Serialize(T message, in Span<byte> buffer, bool bufferProvided)
            {
                return Serialize((TMessage)message, in buffer, bufferProvided);
            }

            protected abstract bool TryCalculateMessageSize(TMessage message, out int size);

            protected abstract Span<byte> Serialize(TMessage message, in Span<byte> buffer, bool bufferProvided);

        }

        public abstract class UnknownSizeMessageBodySerializer<TMessage> : MessageBodySerializer<TMessage> where TMessage : T
        {

            protected override bool TryCalculateMessageSize(TMessage message, out int size)
            {
                size = -1;
                return false;
            }

            protected override Span<byte> Serialize(TMessage message, in Span<byte> buffer, bool bufferProvided)
            {
                if (bufferProvided)
                {
                    throw new Exception("Buffer must not be provided if message body has an unknown size.");
                }

                return Serialize(message);
            }

            protected abstract Span<byte> Serialize(TMessage message);

        }

        public abstract class KnownSizeMessageBodySerializer<TMessage> : MessageBodySerializer<TMessage> where TMessage : T
        {

            protected override bool TryCalculateMessageSize(TMessage message, out int size)
            {
                size = GetMessageSize(message);
                return true;
            }

            protected override Span<byte> Serialize(TMessage message, in Span<byte> buffer, bool bufferProvided)
            {
                if (!bufferProvided)
                {
                    throw new Exception("Buffer must be provided if message body has a known size.");
                }

                Serialize(message, buffer);

                return buffer;
            }

            protected abstract int GetMessageSize(TMessage message);

            protected abstract void Serialize(TMessage message, in Span<byte> buffer);

        }

    }
}
