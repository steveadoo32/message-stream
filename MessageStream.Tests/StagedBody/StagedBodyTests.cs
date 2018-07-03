using System;
using Xunit;
using MessageStream;
using MessageStream.IO;
using System.IO;
using System.Threading.Tasks;
using MessageStream.Serializer;

namespace MessageStream.Tests.StagedBody
{
    public class StagedBodyTests
    {
        
        [Fact(DisplayName = "Tests reading a simple stream of bytes")]
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
                    new StagedBodyMessageSerializer()
                );

            await messageStream.OpenAsync().ConfigureAwait(false);

            // Write two messages
            await messageStream.WriteAsync(new TestMessage
            {
                Header = new StagedBodyMessageHeader
                {
                    MessageId = TestMessage.MessageId,
                    MessageBodyLength = 2
                },
                Value = 2
            }).ConfigureAwait(false);
            await messageStream.WriteAsync(new TestMessage
            {
                Header = new StagedBodyMessageHeader
                {
                    MessageId = TestMessage.MessageId,
                    MessageBodyLength = 2
                },
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
            Assert.Equal(TestMessage.MessageId, result.Result.Header.MessageId);
            Assert.Equal(2, (result.Result as TestMessage).Value);

            var result2 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.False(result2.IsCompleted);
            Assert.Equal(TestMessage.MessageId, result2.Result.Header.MessageId);
            Assert.Equal(4, (result2.Result as TestMessage).Value);

            // This read should signal it's completed.
            var result3 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.True(result3.IsCompleted);
            
            // Close
            await messageStream.CloseAsync().ConfigureAwait(false);

        }
    }
}
