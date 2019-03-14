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
            Func<T, CancellationToken, ValueTask> eventHandler,
            T state, 
            Func<Exception, ValueTask> closeHandler,
            CancellationToken cancellationToken = default);

        EventLoopTask AddEventToLoop(
            Func<CancellationToken, ValueTask> eventHandler,
            Func<Exception, ValueTask> closeHandler,
            CancellationToken cancellationToken = default);

    }
}
