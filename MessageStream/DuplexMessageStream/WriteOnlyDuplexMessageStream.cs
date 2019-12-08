using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.DuplexMessageStream
{
    public class WriteOnlyDuplexMessageStream : IDuplexMessageStream
    {

        private readonly IDuplexMessageStream wrapped;

        // Signals the readasync method to complete
        private TaskCompletionSource<bool> readCompleteCts;

        bool IDuplexMessageStream.ReadCompleted => true;

        bool IDuplexMessageStream.WriteCompleted => wrapped.ReadCompleted;

        string IDuplexMessageStream.StatsString => $"{{ wrappedStream={wrapped.StatsString}, writeOnly=true }}";

        internal WriteOnlyDuplexMessageStream(IDuplexMessageStream wrapped)
        {
            this.wrapped = wrapped;
        }

        async ValueTask<ReadResult> IDuplexMessageStream.ReadAsync(CancellationToken cancellationToken)
        {
            if (readCompleteCts == null || readCompleteCts.Task.IsCompleted)
            {
                return new ReadResult(default, cancellationToken.IsCancellationRequested, true);
            }

            var registration = cancellationToken.Register(() => readCompleteCts.TrySetResult(true)); // we just set result instead of cancelling so no excpetion gets thrown
            await readCompleteCts.Task.ConfigureAwait(false);
            registration.Dispose();

            return new ReadResult(default, cancellationToken.IsCancellationRequested, true);
        }

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed, SequencePosition end)
        {
            // do nothing
        }

        void IDuplexMessageStream.AdvanceReaderTo(SequencePosition consumed)
        {
            // do nothing
        }

        Task IDuplexMessageStream.OpenAsync(CancellationToken cancellationToken)
        {
            this.readCompleteCts = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);
            return wrapped.OpenAsync(cancellationToken);
        }

        Task IDuplexMessageStream.CloseAsync()
        {
            readCompleteCts?.TrySetResult(true);
            return wrapped.CloseAsync();
        }

        ValueTask<FlushResult> IDuplexMessageStream.FlushWriterAsync(CancellationToken cancellationToken)
        {
            return wrapped.FlushWriterAsync(cancellationToken);
        }

        Memory<byte> IDuplexMessageStream.GetWriteMemory(int sizeHint)
        {
            return wrapped.GetWriteMemory(sizeHint);
        }

        ValueTask<FlushResult> IDuplexMessageStream.WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            return wrapped.WriteAsync(memory, cancellationToken);
        }

    }

    public static class WriteOnlyDuplexMessageStreamExtensions
    {
        public static IDuplexMessageStream MakeWriteOnly(this IDuplexMessageStream wrapped)
        {
            if (wrapped is WriteOnlyDuplexMessageStream)
            {
                return wrapped;
            }
            return new WriteOnlyDuplexMessageStream(wrapped);
        }
    }

}
