using System;
using System.Runtime.Serialization;

namespace MessageStream
{
    [Serializable]
    internal class MessageStreamCloseException : Exception
    {
        public MessageStreamCloseException()
        {
        }

        public MessageStreamCloseException(string message) : base(message)
        {
        }

        public MessageStreamCloseException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MessageStreamCloseException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}