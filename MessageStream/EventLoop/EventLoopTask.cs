using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.EventLoop
{

    public class EventLoopTask 
    {

        private readonly Func<CancellationToken, ValueTask> eventHandler;
        private readonly Func<Exception, ValueTask> closeHandler;

        internal CancellationToken CancellationToken { get; }

        internal bool Stopped { get; private set; }

        internal TaskCompletionSource<bool> StoppedTcs { get; private set; }

        public bool IsFaulted { get; private set; }

        public Exception Exception { get; private set; }

        public EventLoopTask(Func<CancellationToken, ValueTask> eventHandler, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
        {
            this.eventHandler = eventHandler;
            this.closeHandler = closeHandler;
            CancellationToken = cancellationToken;
            StoppedTcs = new TaskCompletionSource<bool>();
        }

        internal async ValueTask<bool> LoopAsync()
        {
            if (Stopped || CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            await eventHandler(CancellationToken).ConfigureAwait(false);

            // The event handler might stop the loop, so just return Stopped.
            return Stopped || CancellationToken.IsCancellationRequested;
        }

        internal ValueTask StoppedAsync(Exception ex)
        {
            IsFaulted = ex != null && !CancellationToken.IsCancellationRequested;
            Exception = CancellationToken.IsCancellationRequested ? null : ex;
            StoppedTcs.TrySetResult(true);
            return closeHandler(ex);
        }

        public async ValueTask StopAsync()
        {
            if (Stopped)
            {
                return;
            }

            Stopped = true;

            await StoppedTcs.Task.ConfigureAwait(false);
        }

        public void Stop()
        {
            Stopped = true;
        }

    }

    public class EventLoopTask<T> : EventLoopTask
    {
        
        public EventLoopTask(Func<T, CancellationToken, ValueTask> eventHandler, T state, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
            : base(token => eventHandler(state, token), closeHandler, cancellationToken)
        {
        }

    }

}