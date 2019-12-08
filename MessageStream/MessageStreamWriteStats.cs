using System;
using System.Threading;

namespace MessageStream
{
    public class MessageStreamWriteStats
    {

        private int _messagesWriting;
        private int _messagesOutgoingBufferProcessing;
        private int _messagesWritten;
        private long _bytesWritten;

        public int MessagesWritten => _messagesWritten;

        public int MessagesWriting => _messagesWriting;

        public int MessagesOutgoingBufferProcessing => _messagesOutgoingBufferProcessing;

        public long BytesWritten => _bytesWritten;

        public bool Flushing { get; internal set; }

        public MessageStreamWriteStats()
        {
            Reset();
        }

        internal void Reset()
        {
            _messagesWritten = 0;
            _bytesWritten = 0;
            _messagesWriting = 0;
            _messagesOutgoingBufferProcessing = 0;
            Flushing = false;
        }

        internal void IncMessagesWritten(int incr = 1)
        {
            _messagesWritten += incr;
        }

        internal void IncrBytesWritten(long incr)
        {
            _bytesWritten += incr;
        }

        internal void IncMessagesWriting(int incr = 1)
        {
            _messagesWriting += incr;
        }

        internal void DecMessagesWriting(int DEC = 1)
        {
            _messagesWriting -= DEC;
        }

        internal void IncMessagesOutgoingBufferProcessing(int incr = 1)
        {
            _messagesOutgoingBufferProcessing += incr;
        }

        internal void DecMessagesOutgoingBufferProcessing(int dec = 1)
        {
            _messagesOutgoingBufferProcessing -= dec;
        }

        public override string ToString()
        {
            return $"{{ messagesWriting={MessagesWriting}, messagesOutgoingBufferProcessing={_messagesOutgoingBufferProcessing}, messagesWritten={MessagesWritten}, bytesWritten={BytesWritten}, flushing={Flushing} }}";
        }

    }
}