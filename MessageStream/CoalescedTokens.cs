using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MessageStream
{
    public static class CoalescedTokens
    {
        
        private const uint COALESCING_SPAN_MS = 50;
        
        private readonly static
            ConcurrentDictionary<long, CancellationToken> s_timeToToken =
            new ConcurrentDictionary<long, CancellationToken>();

        public static CancellationToken FromTimeout(TimeSpan timeout)
        {
            return FromTimeout((int) timeout.TotalMilliseconds);
        }

        public static CancellationToken FromTimeout(int millisecondsTimeout)
        {
            if (millisecondsTimeout <= 0)
                return new CancellationToken(true);
                       
            uint currentTime = (uint)Environment.TickCount;
        
            long targetTime = millisecondsTimeout + currentTime;
            targetTime = ((targetTime + (COALESCING_SPAN_MS - 1)) / COALESCING_SPAN_MS) * COALESCING_SPAN_MS;
                       
            CancellationToken token;
            if (!s_timeToToken.TryGetValue(targetTime, out token))
            {
        
                token = new CancellationTokenSource((int)(targetTime - currentTime)).Token;
                if (s_timeToToken.TryAdd(targetTime, token))
        
                {
        
                    token.Register(state => {
        
                        CancellationToken ignored;
        
                        s_timeToToken.TryRemove((long)state, out ignored);
        
                    }, targetTime);
        
                }
            }
            return token;
        }
        
    }
}
