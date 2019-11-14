using System;
using System.Runtime.Serialization;

namespace MessageStream
{
    [Serializable]
    internal class DuplicateRequestException : Exception
    {
        public DuplicateRequestException()
        {
        }

        public DuplicateRequestException(string message) : base(message)
        {
        }

        public DuplicateRequestException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DuplicateRequestException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}