using System.Threading;

namespace MessageStream
{
    public class ConcurrentMessageStreamChannelStats
    {

        private int _channelMessagesRead;
        private int _channelMessagesSubmitted;
        
        public int ChannelMessagesSubmitted => _channelMessagesSubmitted;

        public int ChannelMessagesRead => _channelMessagesRead;
        
        public ConcurrentMessageStreamChannelStats()
        {
            Reset();
        }

        internal void Reset()
        {
            _channelMessagesRead = 0;
            _channelMessagesSubmitted = 0;
        }

        internal void IncReadChannelMessagesRead(int incr = 1)
        {
            _channelMessagesRead += incr;
        }

        internal void IncReadChannelMessagesSubmitted(int incr = 1)
        {
            Interlocked.Add(ref _channelMessagesSubmitted, incr);
        }

        public override string ToString()
        {
            return $"{{ channelMessagesRead={ChannelMessagesRead}, channelMessagesSubmitted={ChannelMessagesSubmitted} }}";
        }

    }
}