using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream
{
    public struct MessageWriteResult
    {

        public bool IsCompleted { get; internal set; }

        public bool Error { get; internal set; }

        public Exception Exception { get; internal set; }

    }
}
