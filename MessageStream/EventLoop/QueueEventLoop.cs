using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessageStream.EventLoop
{
    public class QueueEventLoop : IEventLoop
    {

        private const int DefaultBufferSize = 1024;

        private static readonly ChannelOptions DefaultChannelOptions = new BoundedChannelOptions(DefaultBufferSize);

        private ChannelOptions channelOptions;
        private readonly int readers;

        private object initializeLock = new object();

        /// <summary>
        /// All we do is keep queing the same task onto this channel when the tasks LoopAsync method returns. This can have concurrent readers
        /// </summary>
        private Channel<EventLoopTask> loopChannel;

        public QueueEventLoop(ChannelOptions channelOptions = null, int readers = 1)
        {
            this.channelOptions = channelOptions ?? DefaultChannelOptions;
            this.readers = readers;
        }

        private void EnsureInitialized()
        {
            if (loopChannel != null)
            {
                return;
            }

            lock (initializeLock)
            {
                if (loopChannel != null)
                {
                    return;
                }

                Initialize();
            }
        }

        private void Initialize()
        {
            Channel<TInner> GetChannel<TInner>(ChannelOptions options)
            {
                if (options is UnboundedChannelOptions)
                {
                    return Channel.CreateUnbounded<TInner>((UnboundedChannelOptions)options);
                }
                else
                {
                    return Channel.CreateBounded<TInner>((BoundedChannelOptions)options);
                }
            }

            loopChannel = GetChannel<EventLoopTask>(channelOptions);

            // We can reuse our while event loop here to run the readers
            IEventLoop whileEventLoop = new WhileEventLoop();
            for (int i = 0; i < readers; i++)
            {
                whileEventLoop.AddEventToLoop(ProcessEventLoopTaskAsync, CloseEventLoopTaskProcessorAsync);
            }
        }

        private async ValueTask<bool> ProcessEventLoopTaskAsync(CancellationToken cancellationToken)
        {
            var reader = loopChannel.Reader;

            EventLoopTask result = default;
            bool readable = false;

            while (readable = await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // If its still open and try read is false, we need to wait to read again
                if (!reader.TryRead(out result))
                {
                    continue;
                }

                ProcessEventLoopTask(result, cancellationToken);
            }

            return result == null;
        }

        private async void ProcessEventLoopTask(EventLoopTask eventLoopTask, CancellationToken cancellationToken)
        {
            Exception loopEx = null;
            bool success = true;
            try
            {
                var stop = await eventLoopTask.LoopAsync().ConfigureAwait(false);

                if (stop)
                {
                    try
                    {
                        await eventLoopTask.StoppedAsync(loopEx).ConfigureAwait(false);
                    }
                    catch (Exception stopEx)
                    {
                        // TODO log this?
                    }
                    return;
                }

                await loopChannel.Writer.WriteAsync(eventLoopTask, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                success = cancellationToken.IsCancellationRequested;
                loopEx = cancellationToken.IsCancellationRequested ? null : ex;

                try
                {
                    await eventLoopTask.StoppedAsync(loopEx).ConfigureAwait(false);
                }
                catch (Exception stopEx)
                {
                    // TODO log this?
                }
            }
        }

        private ValueTask CloseEventLoopTaskProcessorAsync(Exception ex)
        {
            return new ValueTask();
        }

        EventLoopTask<T> IEventLoop.AddEventToLoop<T>(Func<T, CancellationToken, ValueTask<bool>> eventHandler, Func<T, Exception, ValueTask> closeHandler, T state, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var eventLoopTask = new EventLoopTask<T>(eventHandler, state, closeHandler, cancellationToken);
            // ?? is this right
            Task.Run(async () =>
            {
                await loopChannel.Writer.WriteAsync(eventLoopTask, cancellationToken).ConfigureAwait(false);
            });

            return eventLoopTask;
        }

        EventLoopTask IEventLoop.AddEventToLoop(Func<CancellationToken, ValueTask<bool>> eventHandler, Func<Exception, ValueTask> closeHandler, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var eventLoopTask = new EventLoopTask(eventHandler, closeHandler, cancellationToken);
            // ?? is this right
            Task.Run(async () =>
            {
                await loopChannel.Writer.WriteAsync(eventLoopTask, cancellationToken).ConfigureAwait(false);
            });

            return eventLoopTask;
        }

    }
}
