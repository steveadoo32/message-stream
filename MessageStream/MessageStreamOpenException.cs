using System;
using System.Runtime.Serialization;

namespace MessageStream
{
    [Serializable]
    internal class MessageStreamOpenException : Exception
    {
        public MessageStreamOpenException()
        {
        }

        public MessageStreamOpenException(string message) : base(message)
        {
        }

        public MessageStreamOpenException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MessageStreamOpenException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}