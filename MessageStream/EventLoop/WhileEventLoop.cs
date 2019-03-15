using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.EventLoop
{
    /// <summary>
    /// This event loop is better for a client connecting to a server(only one loop really).
    /// Use the QueueEventLoop if you expect multiple connections(eg, a server)
    /// </summary>
    public class WhileEventLoop : IEventLoop
    {

        EventLoopTask<T> IEventLoop.AddEventToLoop<T>(Func<T, CancellationToken, ValueTask> eventHandler, T state, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
        {
            var eventLoopTask = new EventLoopTask<T>(eventHandler, state, closeHandler, cancellationToken);
            
            RunEventLoop(eventLoopTask, cancellationToken);

            return eventLoopTask;
        }

        EventLoopTask IEventLoop.AddEventToLoop(Func<CancellationToken, ValueTask> eventHandler, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
        {
            var eventLoopTask = new EventLoopTask(eventHandler, closeHandler, cancellationToken);

            RunEventLoop(eventLoopTask, cancellationToken);

            return eventLoopTask;
        }

        private static void RunEventLoop(EventLoopTask eventLoopTask, CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                Exception loopEx = null;
                bool success = true;
                try
                {
                    while (!eventLoopTask.Stopped)
                    {
                        var stop = await eventLoopTask.LoopAsync().ConfigureAwait(false);
                        if (stop)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    success = cancellationToken.IsCancellationRequested;
                    loopEx = cancellationToken.IsCancellationRequested ? null : ex;
                }

                try
                {
                    await eventLoopTask.StoppedAsync(loopEx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // TODO log this?
                }
            });
        }
    }
}
