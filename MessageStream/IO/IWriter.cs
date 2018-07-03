using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.IO
{
    public interface IWriter
    {

        ValueTask WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken);

    }
}