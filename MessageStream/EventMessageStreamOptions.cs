using System;
using System.Threading.Tasks;

namespace MessageStream
{
    public class EventMessageStreamOptions : ConcurrentMessageStreamOptions
    {
        
        // Where does the event loop run
        public TaskFactory EventLoopTaskFactory { get; set; } = Task.Factory;

        public TaskCreationOptions EventLoopTaskCreationOptions { get; set; } = TaskCreationOptions.LongRunning;

        public bool HandleMessagesOffEventLoop { get; set; } = true;

        // Where do messages from the event loop run
        public TaskFactory EventTaskFactory { get; set; } = Task.Factory;

        public TaskCreationOptions EventTaskCreationOptions { get; set; } = TaskCreationOptions.LongRunning;

        // Where do keep alive tasks run
        public TaskFactory KeepAliveTaskFactory { get; set; } = Task.Factory;

        public TaskCreationOptions KeepAliveTaskCreationOptions { get; set; } = TaskCreationOptions.None;

        public TimeSpan? KeepAliveInterval { get; set; }

        public int NumberReaders { get; set; } = 1;

        public TimeSpan? ReaderCloseTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}