using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Tests.StagedBody
{
    public interface IStagedBodyMessage
    {

        StagedBodyMessageHeader Header { get; set; }

    }
}
