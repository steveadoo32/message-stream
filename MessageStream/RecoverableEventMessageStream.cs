using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MessageStream.Message;
using Microsoft.Extensions.Logging;

namespace MessageStream
{
    public class RecoverableEventMessageStream<T>
    {

        public delegate EventMessageStream<T> GetStream(UnexpectedCloseDelegateAsync unexpectedCloseDelegate);

        public delegate ValueTask StreamOpenedAsync(EventMessageStream<T> stream);

        public delegate ValueTask StreamClosedAsync(EventMessageStream<T> stream);

        private readonly TimeSpan oldStreamCloseTimeout;
        private readonly TimeSpan reconnectBackoff;
        private readonly int maxReconnectAttempts;
        private readonly GetStream getStreamDelegate;
        private readonly StreamOpenedAsync openDelegate;
        private readonly StreamClosedAsync closeDelegate;
        private readonly UnexpectedCloseDelegateAsync unexpectedCloseDelegate;

        private int numReconnectAttempts;
        // private TaskCompletionSource<bool> streamRecoverTimeoutSignal;
        private TaskCompletionSource<EventMessageStream<T>> streamRecoveredSignal;

        public EventMessageStream<T> ActiveStream { get; private set; }

        public RecoverableEventMessageStream(
            TimeSpan oldStreamCloseTimeout,
            TimeSpan reconnectBackoff,
            int maxReconnectAttempts,
            GetStream getStreamDelegate,
            StreamOpenedAsync openDelegate,
            StreamClosedAsync closeDelegate,
            UnexpectedCloseDelegateAsync unexpectedCloseDelegate)
        {
            this.oldStreamCloseTimeout = oldStreamCloseTimeout;
            this.reconnectBackoff = reconnectBackoff;
            this.maxReconnectAttempts = maxReconnectAttempts;
            this.getStreamDelegate = getStreamDelegate;
            this.openDelegate = openDelegate;
            this.closeDelegate = closeDelegate;
            this.unexpectedCloseDelegate = unexpectedCloseDelegate;
        }

        public async Task OpenAsync()
        {
            streamRecoveredSignal = new TaskCompletionSource<EventMessageStream<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            // streamRecoverTimeoutSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventMessageStream<T> newStream = null;
            newStream = ActiveStream = getStreamDelegate((innerEx) => HandleUnexpectedCloseAsync(newStream, innerEx));
            await ActiveStream.OpenAsync().ConfigureAwait(false);
            await openDelegate(ActiveStream).ConfigureAwait(false);
        }
        
        public async Task<MessageWriteRequestResult<TReply>> WriteRequestAsync<TRequest, TReply>(TRequest request, Func<T, MessageWriteRequestResult<TReply>, bool> shouldRetry, TimeSpan timeout = default, bool flush = true) where TRequest : T, IRequest where TReply : T, IResponse
        {
            var stream = ActiveStream;
            // var recoveredTimeoutSignal = streamRecoverTimeoutSignal;
            var recoveredSignal = streamRecoveredSignal;
            MessageWriteRequestResult<TReply> result = await stream.WriteRequestAsync<TRequest, TReply>(request, timeout, flush).ConfigureAwait(false);

            if (shouldRetry(request, result))
            {
                var recoveredTask = recoveredSignal.Task;

                // We don't need to await if these are completed.
                if (recoveredTask.IsCompleted && recoveredTask.Result == null)
                {
                    return result;
                }

                if (!stream.Open || (result.IsCompleted && !result.Result.ReadResult)) // not sure about this check
                {
                    await Task.WhenAny(Task.Delay(reconnectBackoff * numReconnectAttempts), recoveredTask).ConfigureAwait(false);
                    if (!recoveredTask.IsCompleted || recoveredTask.Result == null)
                    {
                        return result;
                    }
                    else
                    {
                        stream = recoveredTask.Result;
                    }
                }

                // we are good to retry. we'll let this one throw if needed
                result = await stream.WriteRequestAsync<TRequest, TReply>(request, timeout, flush).ConfigureAwait(false);
            }

            return result;
        }

        private ValueTask HandleUnexpectedCloseAsync(EventMessageStream<T> oldStream, Exception ex)
        {
            // we have to run this off thread because we will block the old streams close method.
            Task.Run(async () =>
            {
                oldStream.Logger?.LogError(ex, "MessageStream errored. Attempting to recover...");

                try
                {
                    await closeDelegate(oldStream).ConfigureAwait(false);
                    await Task.WhenAny(Task.Delay(oldStreamCloseTimeout), oldStream.CloseAsync()).ConfigureAwait(false);
                }
                catch (Exception closeEx)
                {
                    oldStream.Logger?.LogError(closeEx, "Error closing old message stream");
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
                        newStream = getStreamDelegate((innerEx) => success ? HandleUnexpectedCloseAsync(newStream, innerEx) : new ValueTask());
                        await newStream.OpenAsync().ConfigureAwait(false);
                        await openDelegate(newStream).ConfigureAwait(false);
                        // Success
                        numReconnectAttempts = 0;
                        success = true;
                    }
                    catch (Exception reconnectEx)
                    {
                        // We override the orig exception with the latest one
                        oldStream.Logger?.LogError(ex = reconnectEx, $"Error recovering message stream after {numReconnectAttempts} attempts.");
                    }
                }

                // if we didnt succeed then close the new stream and send the disconnect event
                if (!success)
                {
                    streamRecoveredSignal.TrySetResult(null);
                    try
                    {
                        await closeDelegate(newStream).ConfigureAwait(false);
                        await Task.WhenAny(Task.Delay(oldStreamCloseTimeout), newStream.CloseAsync()).ConfigureAwait(false);
                        await unexpectedCloseDelegate(ex).ConfigureAwait(false);
                        oldStream.Logger?.LogWarning($"Closed message stream after {numReconnectAttempts} reconnection attempts.");
                    }
                    catch (Exception closeEx)
                    {
                        oldStream.Logger?.LogError(closeEx, "Error closing new message stream.");
                    }
                }
                else
                {
                    ActiveStream = newStream;
                    streamRecoveredSignal.TrySetResult(newStream);
                    streamRecoveredSignal = new TaskCompletionSource<EventMessageStream<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    newStream.Logger?.LogInformation("MessageStream recovered.");
                }
            });
            return new ValueTask();
        }

        public async Task CloseAsync()
        {
            streamRecoveredSignal.TrySetResult(null);
            await closeDelegate(ActiveStream).ConfigureAwait(false);
            await ActiveStream.CloseAsync().ConfigureAwait(false);
        }

    }
}
