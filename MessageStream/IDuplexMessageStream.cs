using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream
{
    public interface IDuplexMessageStream
    {

        bool ReadCompleted { get; }

        bool WriteCompleted { get; }

        string StatsString { get; }
        
        Task OpenAsync(CancellationToken cancellationToken);

        Task CloseAsync();

        ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default);

        void AdvanceReaderTo(SequencePosition consumed, SequencePosition end);

        void AdvanceReaderTo(SequencePosition consumed);

        Memory<byte> GetWriteMemory(int sizeHint = 0);

        ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken);

        ValueTask<FlushResult> FlushWriterAsync(CancellationToken cancellationToken);

    }
}
