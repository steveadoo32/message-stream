using MessageStream.Benchmark.StagedBody;
using MessageStream.IO;
using MessageStream.Message;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MessageStream.Benchmark
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            const int messageCount = 1_000_000;
            const int iterations = 10;

            int messageCounter = 0;
            double messageParseTimeTotal = 0;
            var stopwatch = new Stopwatch();

            var messageProvider = new MessageProvider<int, IStagedBodyMessage>();

            var readStream = await GetReadStreamAsync(messageCount).ConfigureAwait(false);
            var writeStream = new MemoryStream();

            var messageStream = new MessageStream<IStagedBodyMessage>(
                    new MessageStreamReader(readStream),
                    new StagedBodyMessageDeserializer(
                        messageProvider,
                        new TestMessage2Deserializer(),
                        new TestMessageDeserializer()
                    ),
                    new MessageStreamWriter(writeStream),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer(),
                        new TestMessage2Serializer()
                    )
                );

            Console.WriteLine();

            for (int i = 0; i < iterations; i++)
            {
                readStream.Position = 0;
                writeStream.Position = 0;

                messageCounter = 0;
                messageParseTimeTotal = 0;

                await messageStream.OpenAsync().ConfigureAwait(false);

                MessageReadResult<IStagedBodyMessage> messageResult;

                stopwatch.Restart();

                while (true)
                {
                    messageResult = await messageStream.ReadAsync().ConfigureAwait(false);

                    if (messageResult.IsCompleted)
                    {
                        break;
                    }

                    messageParseTimeTotal += (messageResult.ParsedTimeUtc - messageResult.ReceivedTimeUtc).TotalMilliseconds;
                    messageCounter++;
                }

                if (messageStream.Open)
                {
                    await messageStream.CloseAsync().ConfigureAwait(false);
                }

                if (messageResult.Error)
                {
                    Console.WriteLine($"Error reading message stream.");
                }

                stopwatch.Stop();

                Console.WriteLine($"Done iteration: {messageCounter / stopwatch.Elapsed.TotalSeconds} messages/s. Avg message parse time: {messageParseTimeTotal / messageCounter}. {messageCounter} total messages read.");
            }

            readStream.Dispose();
            writeStream.Dispose();
        }

        private static async Task<Stream> GetReadStreamAsync(int messageCount)
        {
            var memoryStream = new MemoryStream();
            var messageStream = new MessageStream<IStagedBodyMessage>(
                           new MessageStreamReader(new MemoryStream()),
                           new StagedBodyMessageDeserializer(
                               new MessageProvider<int, IStagedBodyMessage>(),
                               new TestMessage2Deserializer(),
                               new TestMessageDeserializer()
                           ),
                           new MessageStreamWriter(memoryStream),
                           new StagedBodyMessageSerializer(
                               new TestMessageSerializer(),
                               new TestMessage2Serializer()
                           )
                       );
            var stopwatch = new Stopwatch();

            await messageStream.OpenAsync().ConfigureAwait(false);

            stopwatch.Start();

            var random = new Random();
            for (int i = 0; i < messageCount; i++)
            {
                await messageStream.WriteAsync(new TestMessage2
                {
                    Value = 5 //(uint)random.Next(int.MaxValue)
                }).ConfigureAwait(false);
            }

            stopwatch.Stop();
            Console.WriteLine($"Took {stopwatch.ElapsedMilliseconds}ms to write {messageCount} messages. {messageCount / stopwatch.Elapsed.TotalSeconds} messages/s");

            await messageStream.CloseAsync().ConfigureAwait(false);

            memoryStream.Position = 0;

            return memoryStream;
        }

    }
}
