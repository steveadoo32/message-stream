using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Benchmark.StagedBody
{
    public interface IStagedBodyMessage
    {
        
        short MessageId { get; }

    }
}
