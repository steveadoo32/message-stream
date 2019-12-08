using System.Threading;

namespace MessageStream
{

    public class MessageStreamReadStats
    {

        private int _messagesRead;
        private long _bytesRead;
        private int _messagesIncomingBufferProcessing;

        public int MessagesRead => _messagesRead;

        public long BytesRead => _bytesRead;

        public MessageStreamReadStats()
        {
            Reset();
        }

        internal void Reset()
        {
            _messagesRead = 0;
            _messagesIncomingBufferProcessing = 0;
            _bytesRead = 0;
        }

        internal void IncMessagesRead(int incr = 1)
        {
            _messagesRead += incr;
        }

        internal void IncrBytesRead(long incr)
        {
            _bytesRead += incr;
        }

        internal void IncMessagesIncomingBufferProcessing(int incr = 1)
        {
            _messagesIncomingBufferProcessing += incr;
        }

        internal void DecMessagesIncomingBufferProcessing(int incr = 1)
        {
            _messagesIncomingBufferProcessing -= incr;
        }

        public override string ToString()
        {
            return $"{{ messagesRead={MessagesRead}, bytesRead={BytesRead}, messagesIncomingBufferProcessing={_messagesIncomingBufferProcessing} }}";
        }

    }

}