using System;

namespace MessageStream
{
    public struct MessageReadResult<T>
    {

        /// <summary>
        /// True if there is no more data
        /// </summary>
        public bool IsCompleted { get; set; }

        /// <summary>
        /// If there was an error reading
        /// </summary>
        public bool Error { get; set; }

        /// <summary>
        /// Exception encountered when reading
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Read result
        /// </summary>
        public T Result { get; set; }

        /// <summary>
        /// Did a result actually get read.
        /// </summary>
        public bool ReadResult { get; set; }

        public DateTime ReceivedTimeUtc { get; set; }

        public DateTime ParsedTimeUtc { get; set; }

    }
}