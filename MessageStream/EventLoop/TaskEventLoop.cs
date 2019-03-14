using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.EventLoop
{
    public class TaskEventLoop : IEventLoop
    {

        EventLoopTask<T> IEventLoop.AddEventToLoop<T>(Func<T, CancellationToken, ValueTask> eventHandler, T state, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
        {
            var eventLoopTask = new EventLoopTask<T>(eventHandler, state, closeHandler, cancellationToken);

            // Run this async, it'll only create this task once.
            Task.Run(() => ProcessEventLoopTaskAsync(eventLoopTask));

            return eventLoopTask;
        }

        EventLoopTask IEventLoop.AddEventToLoop(Func<CancellationToken, ValueTask> eventHandler, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
        {
            var eventLoopTask = new EventLoopTask(eventHandler, closeHandler, cancellationToken);

            // Run this async, it'll only create this task once.
            Task.Run(() => ProcessEventLoopTaskAsync(eventLoopTask));

            return eventLoopTask;
        }

        private void ProcessEventLoopTaskAsync<T>(EventLoopTask<T> eventLoopTask)
        {
            try
            {
                var task = eventLoopTask.LoopAsync();
                if (task.IsCompleted)
                {
                    ProcessEventLoopTaskResult(eventLoopTask, task.IsCompletedSuccessfully ? task.Result : true, task.IsCanceled, task.IsFaulted, task.IsFaulted ? task.AsTask().Exception : null);
                    return;
                }
                task.AsTask().ContinueWith(ProcessEventLoopTaskResult<T>, eventLoopTask);
            }
            catch (Exception ex)
            {
                ProcessEventLoopTaskResult(eventLoopTask, true, false, true, ex);
            }
        }

        private void ProcessEventLoopTaskResult<T>(Task<bool> task, object eventLoopTask)
        {
            ProcessEventLoopTaskResult((EventLoopTask<T>) eventLoopTask, task.IsCompletedSuccessfully ? task.Result : true, task.IsCanceled, task.IsFaulted, task.IsFaulted ? task.Exception : null);
        }

        private async void ProcessEventLoopTaskResult<T>(EventLoopTask<T> task, bool stop, bool cancelled, bool faulted, Exception ex)
        {
            if (stop || cancelled || faulted)
            {
                try
                {
                    cancelled = cancelled || task.CancellationToken.IsCancellationRequested;
                    await task.StoppedAsync(cancelled ? null : ex).ConfigureAwait(false);
                }
                catch(Exception stopEx)
                {
                    // LOG.
                }
                return;
            }
            ProcessEventLoopTaskAsync(task);
        }
    }
}
