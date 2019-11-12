using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using MessageStream.IO;
using MessageStream.Message;

namespace MessageStream
{
    public class RecoverableEventMessageStream<T>
    {

        private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public delegate EventMessageStream<T> GetStream(EventMessageStream<T>.HandleDisconnectionAsync disconnectionEvent);

        public delegate ValueTask StreamOpenedAsync(EventMessageStream<T> stream);

        public delegate ValueTask StreamClosedAsync(EventMessageStream<T> stream);

        private readonly TimeSpan oldStreamCloseTimeout;
        private readonly TimeSpan reconnectBackoff;
        private readonly int maxReconnectAttempts;
        private readonly GetStream getStreamDelegate;
        private readonly StreamOpenedAsync openDelegate;
        private readonly StreamClosedAsync closeDelegate;
        private readonly EventMessageStream<T>.HandleDisconnectionAsync disconnectionDelegate;

        private int numReconnectAttempts;
        private TaskCompletionSource<bool> streamRecoveredSignal;

        public EventMessageStream<T> ActiveStream { get; private set; }

        public RecoverableEventMessageStream(
            TimeSpan oldStreamCloseTimeout,
            TimeSpan reconnectBackoff,
            int maxReconnectAttempts,
            GetStream getStreamDelegate,
            StreamOpenedAsync openDelegate,
            StreamClosedAsync closeDelegate,
            EventMessageStream<T>.HandleDisconnectionAsync disconnectionDelegate)
        {
            this.oldStreamCloseTimeout = oldStreamCloseTimeout;
            this.reconnectBackoff = reconnectBackoff;
            this.maxReconnectAttempts = maxReconnectAttempts;
            this.getStreamDelegate = getStreamDelegate;
            this.openDelegate = openDelegate;
            this.closeDelegate = closeDelegate;
            this.disconnectionDelegate = disconnectionDelegate;
        }

        public async Task OpenAsync()
        {
            streamRecoveredSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventMessageStream<T> newStream = null;
            newStream = ActiveStream = getStreamDelegate((innerEx, innerExpected) => HandleDisconnectionAsync(newStream, innerEx, innerExpected));
            await ActiveStream.OpenAsync().ConfigureAwait(false);
            await openDelegate(ActiveStream).ConfigureAwait(false);
        }

        public async ValueTask<MessageWriteRequestResult<TReply>> WriteRetryableRequestAsync<TReply>(T message, Func<T, MessageWriteRequestResult<TReply>, Exception, bool> shouldRetry, Func<T, bool> matchFunc = null, TimeSpan timeout = default, bool flush = true) where TReply : T
        {
            var stream = ActiveStream;
            var recoveredSignal = streamRecoveredSignal;
            MessageWriteRequestResult<TReply> result = default;
            Exception outerEx = null;
            try
            {
                result = await stream.WriteRequestAsync<TReply>(message, matchFunc, timeout, flush).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outerEx = ex;
            }

            if (shouldRetry(message, result, outerEx))
            {
                // if the old stream is done we need to wait for the new one.
                if (stream.ReadCompleted || stream.WriteCompleted)
                {
                    var recoveredTask = recoveredSignal.Task;
                    await Task.WhenAny(Task.Delay(timeout), recoveredTask).ConfigureAwait(false);
                    if (!recoveredTask.IsCompleted || !recoveredTask.Result)
                    {
                        if (outerEx != null)
                        {
                            throw outerEx;
                        }
                        return result;
                    }
                }

                // we are good to retry. we'll let this one throw if needed
                result = await ActiveStream.WriteRequestAsync<TReply>(message, matchFunc, timeout, flush).ConfigureAwait(false);
            }

            return result;
        }

        private ValueTask HandleDisconnectionAsync(EventMessageStream<T> oldStream, Exception ex, bool expected)
        {
            // we have to run this off thread because we will block the old streams close method.
            Task.Run(async () =>
            {
                // if we expected an error dont do anything
                if (expected)
                {
                    await disconnectionDelegate(ex, expected).ConfigureAwait(false);
                    return;
                }

                Logger.Error(ex, "MessageStream errored. Attempting to recover...");

                try
                {
                    await closeDelegate(oldStream).ConfigureAwait(false);
                    await Task.WhenAny(Task.Delay(oldStreamCloseTimeout), oldStream.CloseAsync()).ConfigureAwait(false);
                }
                catch (Exception closeEx)
                {
                    Logger.Error(closeEx, "Error closing old message stream");
                }

                bool success = false;
                EventMessageStream<T> newStream = null;
                while (!success && numReconnectAttempts < maxReconnectAttempts)
                {
                    try
                    {
                        await Task.Delay((int)(numReconnectAttempts * reconnectBackoff.TotalMilliseconds)).ConfigureAwait(false);
                        numReconnectAttempts++;
                        // we dont want to handle the disconnects until we've successfully reopened
                        newStream = getStreamDelegate((innerEx, innerExpected) => success ? HandleDisconnectionAsync(newStream, innerEx, innerExpected) : new ValueTask());
                        await newStream.OpenAsync().ConfigureAwait(false);
                        await openDelegate(newStream).ConfigureAwait(false);
                        // Success
                        numReconnectAttempts = 0;
                        success = true;
                    }
                    catch (Exception reconnectEx)
                    {
                        // We override the orig exception with the latest one
                        Logger.Error(ex = reconnectEx, $"Error recovering message stream after {numReconnectAttempts} attempts.");
                    }
                }

                // if we didnt succeed then close the new stream and send the disconnect event
                if (!success)
                {
                    streamRecoveredSignal.TrySetResult(false);
                    try
                    {
                        await closeDelegate(newStream).ConfigureAwait(false);
                        await Task.WhenAny(Task.Delay(oldStreamCloseTimeout), newStream.CloseAsync()).ConfigureAwait(false);
                        await disconnectionDelegate(ex, false).ConfigureAwait(false);
                        Logger.Warn($"Closed message stream after {numReconnectAttempts} reconnection attempts.");
                    }
                    catch (Exception closeEx)
                    {
                        Logger.Error(closeEx, "Error closing new message stream.");
                    }
                }
                else
                {
                    ActiveStream = newStream;
                    streamRecoveredSignal.TrySetResult(true);
                    streamRecoveredSignal = new TaskCompletionSource<bool>();
                    Logger.Info("MessageStream recovered.");
                }
            });
            return new ValueTask();
        }

        public async Task CloseAsync()
        {
            await closeDelegate(ActiveStream).ConfigureAwait(false);
            await ActiveStream.CloseAsync().ConfigureAwait(false);
        }

    }
}
