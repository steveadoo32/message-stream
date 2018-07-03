using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.IO
{
    public class MessageStreamWriter : IWriter
    {

        public Stream Stream { get; }

        public MessageStreamWriter(Stream stream)
        {
            Stream = stream;
        }

        ValueTask IWriter.WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            return Stream.WriteAsync(memory, cancellationToken);
        }

    }
}