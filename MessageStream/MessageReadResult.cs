using System;

namespace MessageStream
{
    public struct MessageReadResult<T>
    {

        /// <summary>
        /// True if there is no more data
        /// </summary>
        public bool IsCompleted { get; internal set; }

        /// <summary>
        /// If there was an error reading
        /// </summary>
        public bool Error { get; internal set; }

        /// <summary>
        /// Exception encountered when reading
        /// </summary>
        public Exception Exception { get; internal set; }

        /// <summary>
        /// Read result
        /// </summary>
        public T Result { get; internal set; }

    }
}