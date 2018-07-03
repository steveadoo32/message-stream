using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.IO
{
    public class MessageStreamReader : IReader
    {
        
        public Stream Stream { get; }

        public MessageStreamReader(Stream stream)
        {
            Stream = stream;
        }

        ValueTask<int> IReader.ReadAsync(Memory<byte> memory, CancellationToken cancellationToken)
        {
            return Stream.ReadAsync(memory, cancellationToken);
        }

    }
}