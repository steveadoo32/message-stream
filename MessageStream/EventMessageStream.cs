using MessageStream;
using MessageStream.Message;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessageStream
{
    /// <summary>
    /// Spawns tasks that will read from the readers and send events to you.
    /// Useful for a socket connection or something.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class EventMessageStream<T> : ConcurrentMessageStream<T>
    {

        public delegate ValueTask<bool> HandleMessageAsync(T message);
        
        public delegate ValueTask HandleKeepAliveAsync();

        private static ChannelOptions CreateDefaultReaderChannelOptions(int numReaders)
        {
            // if one reader, we can optimize a bit
            if (numReaders == 1)
            {
                return new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true
                };
            }

            // let concurrent message stream decide
            return null;
        }

        private readonly EventMessageStreamOptions eventOptions;
        private readonly HandleMessageAsync handleMessageDelegate;
        private readonly HandleKeepAliveAsync handleKeepAliveDelegate;
        private CancellationTokenSource closeCts;

        private List<Task> readerTasks;
        private Task<Task> keepAliveTask;

        /// <summary>
        /// </summary>
        /// <param name="handleMessageDelegate">Handles messages</param>
        /// <param name="handleDisconnectionDelegate">Called when there is a problem with the reader. The stream will be closed before this is called.</param>
        /// <param name="handleKeepAliveDelegate">Useful if you need to keep writing data every xxx seconds</param>
        /// <param name="numReaders">How many tasks to spawn to read from the channel</param>
        /// <param name="handleMessagesAsynchronously">
        /// Set this to true if you are using a bounded reader channel and you call WriteRequestAsync inside of your handleMessageDelegate.
        /// If you leave it false, you can run into a situation where your reader channel is being throttled, so the responses for WriteRequest will never come back,
        /// which will cause even more blocking on the whole read pipeline.
        /// </param>
        /// <param name="keepAliveTimeSpan">How long until we wait until we invoke the keep alive delegate. Default is 30 seconds.</param>
        public EventMessageStream(
            IMessageDeserializer<T> deserializer,
            IMessageSerializer<T> serializer,
            IDuplexMessageStream duplexMessageStream,
            HandleMessageAsync handleMessageDelegate,
            HandleKeepAliveAsync handleKeepAliveDelegate,
            UnexpectedCloseDelegateAsync unexpectedCloseDelegate,
            EventMessageStreamOptions options = null,
            RequestResponseKeyResolver<T> rpcKeyResolver = null)
            : base(deserializer, serializer, duplexMessageStream, unexpectedCloseDelegate, options ?? new EventMessageStreamOptions(), rpcKeyResolver)
        {
            this.eventOptions = this.options as EventMessageStreamOptions;
            this.handleMessageDelegate = handleMessageDelegate;
            this.handleKeepAliveDelegate = handleKeepAliveDelegate;            
        }
        

        /// <summary>
        /// Starts this message stream on your reader/writers.
        /// You have to open them yourself
        /// </summary>
        /// <returns></returns>
        public override async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            if (Open)
            {
                throw new MessageStreamOpenException("MessageStream already open.");
            }
            
            // Open underlying stream
            await base.OpenAsync(cancellationToken).ConfigureAwait(false);
            
            closeCts = new CancellationTokenSource();
            readerTasks = new List<Task>(eventOptions.NumberReaders);

            for (int i = 0; i < eventOptions.NumberReaders; i++)
            {
                int capturedId = i;
                readerTasks.Add(eventOptions.EventLoopTaskFactory.StartNew(obj => OuterReadLoopAsync((OuterReadState)obj), new OuterReadState
                {
                    stream = this,
                    id = capturedId
                }, eventOptions.EventLoopTaskCreationOptions).Unwrap().ContinueWith(result => Logger?.LogDebug($"EventMessageStream ReadLoop {capturedId} completed. TaskCompleted = {result.IsCompleted}.")));
            }

            // Start the keep alive task
            if (eventOptions.KeepAliveInterval.HasValue)
            {
                keepAliveTask = eventOptions.KeepAliveTaskFactory.StartNew(KeepAliveAsync, eventOptions.KeepAliveTaskCreationOptions);
            }
        }

        public override async Task CloseAsync()
        {
            if (!Open)
            {
                throw new MessageStreamOpenException("MessageStream is not open.");
            }

            try
            {
                await base.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error closing underlying message stream.");
            }

            // Wait for channel clean up tasks to finish
            var readersCompleteTask = Task.WhenAll(readerTasks);
            if (eventOptions.ReaderCloseTimeout.HasValue)
            {
                await Task.WhenAny(Task.Delay(eventOptions.ReaderCloseTimeout.Value), readersCompleteTask).ConfigureAwait(false);
                // Force the readers to complete.
                if (!readersCompleteTask.IsCompleted)
                {
                    Logger?.LogWarning($"Reader Tasks were not completed within {eventOptions.ReaderCloseTimeout}. Forcing closed.");
                }
            }
            else
            {
                await Task.WhenAny(readersCompleteTask).ConfigureAwait(false);
            }

            closeCts.Cancel();

            if (keepAliveTask != null)
            {
                await keepAliveTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// We have to write the code in this way so the compiler doesn't generate a display class that allocates
        /// on every read.
        /// </summary>
        private async Task OuterReadLoopAsync(OuterReadState state)
        {
            while (!closeCts.IsCancellationRequested)
            {
                state.result = await state.stream.ReadAsync().ConfigureAwait(false);
                
                // Check that result isn't null. If it is we have a message we couldn't read.
                if (state.result.ReadResult)
                {
                    try
                    {
                        state.handleTask = state.stream.handleMessageDelegate(state.result.Result);

                        if (!state.stream.eventOptions.HandleMessagesOffEventLoop)
                        {
                            await state.handleTask.ConfigureAwait(false);
                        }
                        else if (!state.handleTask.IsCompleted)
                        {
                            // Handle this result async.
                            _ = state.handleTask.AsTask().ContinueWith(result =>
                            {
                                if (result.IsFaulted)
                                {
                                    Logger?.LogError(result.Exception, "Error handling message");
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogError(ex, "Error handling message");
                    }
                }

                // No more data
                if (state.result.IsCompleted)
                {
                    break;
                }
            }
        }

        private async Task KeepAliveAsync()
        {
            while (Open && !closeCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(eventOptions.KeepAliveInterval.Value, closeCts.Token).ConfigureAwait(false);

                    if (closeCts.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    await eventOptions.KeepAliveTaskFactory.StartNew(() => handleKeepAliveDelegate(), closeCts.Token, eventOptions.KeepAliveTaskCreationOptions, eventOptions.KeepAliveTaskFactory.Scheduler ?? TaskScheduler.Default).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException)
                    {
                        continue;
                    }
                    Logger?.LogError(ex, $"Error executing keep alive for message stream.");
                }
            }
        }

        private class OuterReadState
        {

            public int id;
            public EventMessageStream<T> stream;
            public MessageReadResult<T> result;
            public ValueTask<bool> handleTask;

        }

    }
}
