using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.IO
{
    public interface IReader
    {

        /// <summary>
        /// Reads data into memory.
        /// </summary>
        /// <param name="memory">Read data into this</param>
        /// <returns>The length of read data. Return 0 to signal the end of data.</returns>
        ValueTask<int> ReadAsync(Memory<byte> memory, CancellationToken cts = default);

    }
}
