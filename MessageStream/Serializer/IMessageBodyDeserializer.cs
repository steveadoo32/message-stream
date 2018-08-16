using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Serializer
{

    /// <summary>
    /// DON'T USE THIS. Needed it to get around generics
    /// </summary>
    public interface IMessageBodyDeserializer<TIdentifier>
    {

        TIdentifier Identifier { get; }
        
    }

    /// <summary>
    /// Facilitates deserializing onto a message body. TODO better name for this? Something more generic than IMessageBody
    /// </summary>
    /// <typeparam name="TIdentifier">The identifier of the message</typeparam>
    /// <typeparam name="TState">The state that (most likely) determined the state of the message</typeparam>
    /// <typeparam name="TMessage">The message to deserialize onto</typeparam>
    public interface IMessageBodyDeserializer<TIdentifier, TState, TMessage> : IMessageBodyDeserializer<TIdentifier>
    {
        /// <summary>
        /// Deserializes the buffer onto a message.
        /// There is a bug currently if you declare your implementations like this:
        /// <para>
        /// void IMessageBodyDeserializer<typeparamref name="TIdentifier"/>.DeserializeOnto
        /// </para>
        /// </summary>
        /// <param name="buffer">The buffer with data to deserialize</param>
        /// <param name="state">The state that determined what message type this was. Generally the message header</param>
        /// <param name="message">The message to deserialize onto</param>
        void DeserializeOnto(in ReadOnlySpan<byte> buffer, TState state, ref TMessage message);

    }

}
