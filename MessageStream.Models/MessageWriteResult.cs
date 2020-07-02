using System;

namespace MessageStream
{
    public struct MessageWriteResult
    {

        public bool IsCompleted { get; set; }

        public bool Error { get; set; }

        public Exception Exception { get; set; }

    }

    public struct MessageWriteRequestResult<T>
    {

        public bool IsCompleted { get; set; }

        public bool Error { get; set; }

        public Exception Exception { get; set; }

        public MessageReadResult<T> Result { get; set; }

    }

}
