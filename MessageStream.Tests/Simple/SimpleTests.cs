using MessageStream.IO;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MessageStream.Tests.Simple
{
    public class SimpleTests
    {
        
        [Fact(DisplayName = "Simple Deserializer Read")]
        public async Task TestSimpleStreamAsync()
        {
            var readStream = new MemoryStream();
            var writeStream = new MemoryStream();

            var messageStream = new MessageStream<SimpleMessage>(
                    new MessageStreamReader(readStream),
                    new SimpleMessageDeserializer(),
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

            await messageStream.OpenAsync().ConfigureAwait(false);

            // Read the two messages
            var result = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.False(result.IsCompleted);
            Assert.Equal(1, result.Result.Id);
            Assert.Equal(2, result.Result.Value);

            var result2 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.False(result2.IsCompleted);
            Assert.Equal(2, result2.Result.Id);
            Assert.Equal(4, result2.Result.Value);

            // This read should signal it's completed.
            var result3 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.True(result3.IsCompleted);
            
            // Close
            await messageStream.CloseAsync().ConfigureAwait(false);

        }
    }
}
