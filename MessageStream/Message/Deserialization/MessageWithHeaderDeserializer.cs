using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Message
{
    public abstract class MessageWithHeaderDeserializer<THeader, TIdentifier, T> : StagedDeserializer<T>
    {

        private readonly MessageBodyDeserializer<TIdentifier, T, THeader> messageBodyDeserializer;
        
        public MessageWithHeaderDeserializer(
            IMessageProvider<TIdentifier, T> messageProvider,
            params IMessageBodyDeserializer<TIdentifier>[] deserializers
        )
        {
            messageBodyDeserializer = new MessageBodyDeserializer<TIdentifier, T, THeader>(
                    messageProvider,
                    deserializers
                );
        }

        [DeserializationStage(0)]
        public THeader Deserialize(in ReadOnlySpan<byte> buffer, out int nextStageLength)
        {
            return ReadHeader(in buffer, out nextStageLength);
        }

        [DeserializationStage(1)]
        public T Deserialize(in ReadOnlySpan<byte> buffer, THeader header)
        {
            var message = messageBodyDeserializer.Deserialize(buffer, header, GetMessageIdentifier(header));

            PostProcessMessage(header, message);

            return message;
        }

        /// <summary>
        /// Called after the header and body have been deserialized.
        /// Generally used to set the header on a header property on the actual message.
        /// </summary>
        protected virtual void PostProcessMessage(THeader header, T message) { }

        protected abstract THeader ReadHeader(in ReadOnlySpan<byte> headerBuffer, out int bodyLength);

        protected abstract TIdentifier GetMessageIdentifier(THeader header);

        public interface IStagedBodyMessageBodyDeserializer<TMessage> : IMessageBodyDeserializer<TIdentifier, THeader, TMessage>
        {
        }

    }


}
