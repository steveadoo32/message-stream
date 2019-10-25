using MessageStream;
using MessageStream.IO;
using MessageStream.Message;
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

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        
        public delegate ValueTask<bool> HandleMessageAsync(T message);

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

        /// <summary>
        /// Called when disconnections happen on the reader. This will close the stream.
        /// </summary>
        public delegate ValueTask HandleDisconnectionAsync(Exception ex, bool expected);

        public delegate ValueTask HandleKeepAliveAsync();
        
        private readonly HandleMessageAsync handleMessageDelegate;
        private readonly HandleDisconnectionAsync handleDisconnectionDelegate;
        private readonly HandleKeepAliveAsync handleKeepAliveDelegate;
        
        private List<Task> readTasks;
        private CancellationTokenSource keepAliveCts;
        private Task keepAliveTask;
        private bool closing = false;

        private SemaphoreSlim outerCloseSemaphore;
        
        public int NumReaders { get; }

        public bool HandleMessagesAsynchronously { get; }

        public TimeSpan KeepAliveTimeSpan { get; }

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
            IReader reader,
            IMessageDeserializer<T> deserializer,
            IWriter writer,
            IMessageSerializer<T> serializer,
            HandleMessageAsync handleMessageDelegate,
            HandleDisconnectionAsync handleDisconnectionDelegate,
            HandleKeepAliveAsync handleKeepAliveDelegate,
            int numReaders = 1,
            bool handleMessagesAsynchronously = false,
            TimeSpan? keepAliveTimeSpan = null,
            PipeOptions readerPipeOptions = null, 
            PipeOptions writerPipeOptions = null, 
            TimeSpan? writerCloseTimeout = null, 
            ChannelOptions readerChannelOptions = null, 
            ChannelOptions writerChannelOptions = null, 
            TimeSpan? readerFlushTimeout = null)
            : base(reader, deserializer, writer, serializer, readerPipeOptions, writerPipeOptions, writerCloseTimeout, readerChannelOptions ?? CreateDefaultReaderChannelOptions(numReaders), writerChannelOptions, readerFlushTimeout)
        {
            this.handleMessageDelegate = handleMessageDelegate;
            this.handleDisconnectionDelegate = handleDisconnectionDelegate;
            this.handleKeepAliveDelegate = handleKeepAliveDelegate;
            
            NumReaders = numReaders;
            HandleMessagesAsynchronously = handleMessagesAsynchronously;
            KeepAliveTimeSpan = keepAliveTimeSpan ?? TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Starts this message stream on your reader/writers.
        /// You have to open them yourself
        /// </summary>
        /// <returns></returns>
        public override async Task OpenAsync()
        {
            closing = false;
            
            // Let the underlying message stream infrastructure startup.
            await base.OpenAsync().ConfigureAwait(false);

            outerCloseSemaphore = new SemaphoreSlim(1, 1);

            // Start the read tasks
            readTasks = new List<Task>(NumReaders);

            for(int i = 0; i < NumReaders; i++)
            {
                readTasks.Add(Task.Factory.StartNew(obj => OuterReadLoopAsync((OuterReadState) obj), new OuterReadState
                {
                    stream = this
                }).Unwrap());
            }

            // Start the keep alive task
            keepAliveCts = new CancellationTokenSource();
            keepAliveTask = Task.Run(KeepAliveAsync, keepAliveCts.Token);
        }
        
        public override async Task CloseAsync()
        {
            if (!Open)
            {
                return;
            }

            closing = true;

            await InnerCloseAsync().ConfigureAwait(false);

            outerCloseSemaphore.Dispose();
        }

        protected virtual async Task InnerCloseAsync()
        {
            // Shut down keep alive task.
            keepAliveCts.Cancel(false);
            // Ignore keep alive error.
            await keepAliveTask.ContinueWith(_ => true).ConfigureAwait(false);
            keepAliveTask = null;
            
            await base.CloseAsync().ConfigureAwait(false);
                        
            await Task.WhenAll(readTasks.Select(t =>
            {
                return t.ContinueWith(readTask =>
                {
                    if (readTask.IsFaulted)
                    {
                        Logger.Warn(readTask.Exception, "Error shutting down read task. Ignoring.");
                    }
                });
            })).ConfigureAwait(false);
            
            closing = false;
        }

        private async Task OuterCloseAsync(Exception exception)
        {
            await outerCloseSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                // One of the other read tasks already closed us, so return.
                if (closing || !Open)
                {
                    return;
                }

                bool wasClosing = closing;

                await CloseAsync().ConfigureAwait(false);
                
                await handleDisconnectionDelegate(exception, wasClosing);
            }
            finally
            {
                outerCloseSemaphore.Release();
            }
        }

        /// <summary>
        /// We have to write the code in this way so the compiler doesn't generate a display class that allocates
        /// on every read.
        /// </summary>
        private async Task OuterReadLoopAsync(OuterReadState state)
        {
            while (true)
            {
                state.result = await state.stream.ReadAsync().ConfigureAwait(false);

                // Check that result isn't null. If it is we have a message we couldn't read.
                if (state.result.ReadResult)
                {
                    state.handleTask = state.stream.handleMessageDelegate(state.result.Result);

                    if (!state.stream.HandleMessagesAsynchronously)
                    {
                        await state.handleTask.ConfigureAwait(false);
                    }
                }

                if (state.result.IsCompleted)
                {
                    // We have to run this in an async task becuase CloseAsync blocks on the read tasks
                    // so we could end up in a deadlock.
                    _ = Task.Factory.StartNew(async obj => await (obj as OuterReadState).stream.OuterCloseAsync((obj as OuterReadState).result.Exception), state);

                    break;
                }
            }
        }

        private async Task KeepAliveAsync()
        {
            while(!keepAliveCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(KeepAliveTimeSpan, keepAliveCts.Token).ConfigureAwait(false);

                    if (keepAliveCts.Token.IsCancellationRequested)
                    {
                        break;
                    }
                    
                    await handleKeepAliveDelegate().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error executing keep alive for message stream.");
                }
            }
        }
        
        private class OuterReadState
        {

            public EventMessageStream<T> stream;
            public MessageReadResult<T> result;
            public ValueTask<bool> handleTask;

        }

    }
}
