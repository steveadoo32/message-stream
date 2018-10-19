using MessageStream.IO;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MessageStream.Tests.Simple
{
    public class EventMessageStreamTests
    {

        [Fact(DisplayName = "EventMessageStream works")]
        public async Task TestSimpleStreamAsync()
        {
            var readStream = new MemoryStream();
            var writeStream = new MemoryStream();

            var messageStream = new MessageStream<SimpleMessage>(
                    new MessageStreamReader(readStream),
                    new StructMessageDeserializer(),
                    new MessageStreamWriter(writeStream),
                    new SimpleMessageSerializer()
                );

            await messageStream.OpenAsync().ConfigureAwait(false);

            // Write two messages
            await messageStream.WriteAsync(new SimpleMessage
            {
                Id = 1,
                Value = 2
            }).ConfigureAwait(false);
            await messageStream.WriteAsync(new SimpleMessage
            {
                Id = 2,
                Value = 4
            }).ConfigureAwait(false);

            await messageStream.CloseAsync().ConfigureAwait(false);

            // Reset the streams position so we can read in the messages
            writeStream.Position = 0;
            writeStream.CopyTo(readStream);
            readStream.Position = 0;

            var receivedMessages = new List<SimpleMessage>();
            var closedTcs = new TaskCompletionSource<bool>();

            var eventedMessageStream = new EventMessageStream<SimpleMessage>
            (
                    new MessageStreamReader(readStream),
                    new StructMessageDeserializer(),
                    new MessageStreamWriter(writeStream),
                    new SimpleMessageSerializer(),
                    message =>
                    {
                        receivedMessages.Add(message);
                        return new ValueTask<bool>();
                    },
                    (ex, expected) => // The stream will close because the memory stream will run out of data so ignore results
                    {
                        closedTcs.TrySetResult(true);
                        return new ValueTask();
                    },
                    () => // ignore keep alive.
                    {
                        return new ValueTask();
                    }
            );

            await eventedMessageStream.OpenAsync().ConfigureAwait(false);

            await closedTcs.Task.ConfigureAwait(false);

            Assert.Equal(2, receivedMessages.Count);

            await eventedMessageStream.CloseAsync().ConfigureAwait(false);
        }

    }
}
