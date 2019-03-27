using MessageStream.IO;
using MessageStream.Message;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MessageStream.Tests.StagedBody
{
    public class StagedBodyTests
    {
        
        [Fact(DisplayName = "Staged Deserializer Write/Read")]
        public async Task TestStagedStreamAsync()
        {
            var readStream = new MemoryStream();
            var writeStream = new MemoryStream();

            var messageStream = new MessageStream<IStagedBodyMessage>(
                    new MessageStreamReader(readStream),
                    new StagedBodyMessageDeserializer(
                        new MessageProvider<int, IStagedBodyMessage>(),
                        new TestMessageDeserializer()
                    ),
                    new MessageStreamWriter(writeStream),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer()
                    )
                );

            await messageStream.OpenAsync().ConfigureAwait(false);

            // Write two messages
            await messageStream.WriteAsync(new TestMessage
            {
                Value = 2
            }).ConfigureAwait(false);
            await messageStream.WriteAsync(new TestMessage
            {
                Value = 4
            }).ConfigureAwait(false);

            await messageStream.CloseAsync().ConfigureAwait(false);

            // Reset the streams position so we can read in the messages
            writeStream.Position = 0;
            writeStream.CopyTo(readStream);
            readStream.Position = 0;

            await messageStream.OpenAsync().ConfigureAwait(false);

            // Read the two messages
            var result = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.False(result.IsCompleted);
            Assert.IsType<TestMessage>(result.Result);
            Assert.Equal(2, (result.Result as TestMessage).Value);

            var result2 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.False(result2.IsCompleted);
            Assert.IsType<TestMessage>(result.Result);
            Assert.Equal(4, (result2.Result as TestMessage).Value);

            // This read should signal it's completed.
            var result3 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.True(result3.IsCompleted);
            
            // Close
            await messageStream.CloseAsync().ConfigureAwait(false);

        }

        [Fact(DisplayName = "Multithreaded Staged Deserializer Write/Read")]
        public async Task TestMultiThreadedStagedStream()
        {
            var readStream = new MemoryStream();
            var writeStream = new MemoryStream();

            var messageStream = new ConcurrentMessageStream<IStagedBodyMessage>(
                    new MessageStreamReader(readStream),
                    new StagedBodyMessageDeserializer(
                        new MessageProvider<int, IStagedBodyMessage>(),
                        new TestMessageDeserializer()
                    ),
                    new MessageStreamWriter(writeStream),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer()
                    ),
                    readerFlushTimeout: TimeSpan.FromSeconds(1)
                );

            await messageStream.OpenAsync().ConfigureAwait(false);

            const int messageCount = 100000;
            const int parallellism = 10;
            const int blockSize = messageCount / parallellism;
            var random = new Random();

            await Task.WhenAll(Enumerable.Range(0, parallellism).Select(async _ =>
            {
                for (int i = 0; i < blockSize; i++)
                {
                    await messageStream.WriteAsync(new TestMessage
                    {
                        Value = (short) random.Next(10000)
                    }).ConfigureAwait(false);
                }
            })).ConfigureAwait(false);

            await messageStream.CloseAsync().ConfigureAwait(false);

            // Reset the streams position so we can read in the messages
            writeStream.Position = 0;
            writeStream.CopyTo(readStream);
            readStream.Position = 0;

            await messageStream.OpenAsync().ConfigureAwait(false);

            int actualMessageCount = 0;

            await Task.WhenAll(Enumerable.Range(0, parallellism).Select(async _ =>
            {
                while (true)
                {
                    var result = await messageStream.ReadAsync().ConfigureAwait(false);
                    if (result.ReadResult)
                    {
                        Interlocked.Increment(ref actualMessageCount);
                    }
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            })).ConfigureAwait(false);


            Assert.Equal(messageCount, actualMessageCount);

            // Close
            await messageStream.CloseAsync().ConfigureAwait(false);

        }

        [Fact(DisplayName = "Multithreaded Staged Deserializer Mock message Write/Read")]
        public async Task TestMultiThreadedStagedMockStream()
        {
            var readStream = new MemoryStream();
            var writeStream = new MemoryStream();

            var messageStream = new ConcurrentMessageStream<IStagedBodyMessage>(
                    new MessageStreamReader(readStream),
                    new StagedBodyMessageDeserializer(
                        new MessageProvider<int, IStagedBodyMessage>(),
                        new TestMessageDeserializer()
                    ),
                    new MessageStreamWriter(writeStream),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer()
                    ),
                    readerFlushTimeout: TimeSpan.FromSeconds(1)
                );
            
            await messageStream.OpenAsync().ConfigureAwait(false);

            int messageCount = 5;
            int actualMessageCount = 0;
            for (int i = 0; i < messageCount; i++)
            {
                await messageStream.EnqueueMessageOnReaderAsync(new TestMessage
                {
                    Header = new StagedBodyMessageHeader(),
                    Value = (short) i
                });
            }

            await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                while (true)
                {
                    var result = await messageStream.ReadAsync().ConfigureAwait(false);
                    if (result.ReadResult)
                    {
                        Interlocked.Increment(ref actualMessageCount);
                    }
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            })).ConfigureAwait(false);


            Assert.Equal(messageCount, actualMessageCount);

            // Close
            await messageStream.CloseAsync().ConfigureAwait(false);

        }

    }
}
