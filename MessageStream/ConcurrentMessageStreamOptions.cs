using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessageStream
{
    public class ConcurrentMessageStreamOptions : MessageStreamOptions
    {

        public ChannelOptions ReaderChannelOptions { get; set; } = new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleWriter = true,
            SingleReader = false
        };

        public ChannelOptions WriterChannelOptions { get; set; } = new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        };

        public TaskCreationOptions ReadChannelTaskOptions { get; set; } = TaskCreationOptions.LongRunning;

        public TaskCreationOptions WriteChannelTaskOptions { get; set; } = TaskCreationOptions.LongRunning;

        public TaskFactory ChannelTaskFactory { get; set; } = Task.Factory;

        /// <summary>
        /// Where do responses get executed
        /// </summary>
        public TaskFactory ResponseTaskFactory { get; set; } = Task.Factory;

        public int? WriteMessageCountFlushThreshold { get; set; } = 64;

        public TimeSpan? ChannelCloseTimeout { get; set; } = null;

    }
}