using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.EventLoop
{
    public interface IEventLoop
    {

        EventLoopTask<T> AddEventToLoop<T>(
            Func<T, CancellationToken, ValueTask<bool>> eventHandler,
            Func<T, Exception, ValueTask> closeHandler,
            T state,
            CancellationToken cancellationToken = default);

        EventLoopTask AddEventToLoop(
            Func<CancellationToken, ValueTask<bool>> eventHandler,
            Func<Exception, ValueTask> closeHandler,
            CancellationToken cancellationToken = default);

    }
}
