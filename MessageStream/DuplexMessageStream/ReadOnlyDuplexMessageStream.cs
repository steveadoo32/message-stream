using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.DuplexMessageStream
{
    public class ReadOnlyDuplexMessageStream : IDuplexMessageStream
    {

        private readonly IDuplexMessageStream wrapped;

        bool IDuplexMessageStream.ReadCompleted => wrapped.ReadCompleted;

        bool IDuplexMessageStream.WriteCompleted => true;

        string IDuplexMessageStream.StatsString => $"{{ wrappedStream={wrapped.StatsString}, readOnly=true }}";

        internal ReadOnlyDuplexMessageStream(IDuplexMessageStream wrapped)
        {
            this.wrapped = wrapped;
        }

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed, SequencePosition end)
        {
            this.wrapped.AdvanceReaderTo(consumed, end);
        }

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed)
        {
            this.wrapped.AdvanceReaderTo(consumed);
        }

        Task IDuplexMessageStream.OpenAsync(CancellationToken cancellationToken)
        {
            return wrapped.OpenAsync(cancellationToken);
        }

        Task IDuplexMessageStream.CloseAsync()
        {
            return wrapped.CloseAsync();
        }

        ValueTask<FlushResult> IDuplexMessageStream.FlushWriterAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<FlushResult>(new FlushResult(cancellationToken.IsCancellationRequested, true));
        }

        Memory<byte> IDuplexMessageStream.GetWriteMemory(int sizeHint)
        {
            return new Memory<byte>(new byte[sizeHint]);
        }

        ValueTask<ReadResult> IDuplexMessageStream.ReadAsync(CancellationToken cancellationToken)
        {
            return wrapped.ReadAsync(cancellationToken);
        }

        ValueTask<FlushResult> IDuplexMessageStream.WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            return new ValueTask<FlushResult>(new FlushResult(cancellationToken.IsCancellationRequested, true));
        }

    }

    public static class ReadOnlyDuplexMessageStreamExtensions
    {
        public static IDuplexMessageStream MakeReadOnly(this IDuplexMessageStream wrapped)
        {
            if (wrapped is ReadOnlyDuplexMessageStream)
            {
                return wrapped;
            }
            return new ReadOnlyDuplexMessageStream(wrapped);
        }
    }

}
