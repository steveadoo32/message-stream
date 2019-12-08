using MessageStream.DuplexMessageStream;
using MessageStream.IO;
using MessageStream.Message;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MessageStream.Tests.Structs.StagedBody
{
    public class StagedBodyTests
    {
        
        /// <summary>
        /// This test is kind of useful, the staged body messages mostly require an interface,
        /// so you wouldn't really be using structs.
        /// </summary>
        /// <returns></returns>
        [Fact(DisplayName = "Staged Struct Write/Read")]
        public async Task TestStagedStreamAsync()
        {
            var readStream = new MemoryStream();
            var writeStream = new MemoryStream();

            var messageStream = new MessageStream<TestMessage>(
                    new StagedBodyMessageDeserializer(
                        new MessageProvider<int, TestMessage>(),
                        new TestMessageDeserializer()
                    ),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer()
                    ),
                    new StreamDuplexMessageStream(readStream, writeStream).MakeWriteOnly()
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
            readStream = new MemoryStream(writeStream.ToArray());
            readStream.Position = 0;
            writeStream = new MemoryStream();

            messageStream = new MessageStream<TestMessage>(
                    new StagedBodyMessageDeserializer(
                        new MessageProvider<int, TestMessage>(),
                        new TestMessageDeserializer()
                    ),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer()
                    ),
                    new StreamDuplexMessageStream(readStream, writeStream).MakeReadOnly()
                );

            await messageStream.OpenAsync().ConfigureAwait(false);

            // Read the two messages
            var result = await messageStream.ReadAsync().ConfigureAwait(false);

            Assert.False(result.IsCompleted);
            Assert.Equal(2, result.Result.Value);

            var result2 = await messageStream.ReadAsync().ConfigureAwait(false);

            Assert.False(result2.IsCompleted);
            Assert.Equal(4, result2.Result.Value);

            // This read should signal it's completed.
            var result3 = await messageStream.ReadAsync().ConfigureAwait(false);
            Assert.True(result3.IsCompleted);
            
            // Close
            await messageStream.CloseAsync().ConfigureAwait(false);

        }
        
    }
}
