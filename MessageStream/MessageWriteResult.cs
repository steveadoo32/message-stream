using System;

namespace MessageStream
{
    public struct MessageWriteResult
    {

        public bool IsCompleted { get; internal set; }

        public bool Error { get; internal set; }

        public Exception Exception { get; internal set; }

    }

    public struct MessageWriteRequestResult<T>
    {

        public bool IsCompleted { get; internal set; }

        public bool Error { get; internal set; }

        public Exception Exception { get; internal set; }

        public MessageReadResult<T> Result { get; set; }

    }

}
