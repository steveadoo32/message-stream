using MessageStream.Benchmark.StagedBody;
using MessageStream.DuplexMessageStream;
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
            const int messageCount = 10_000_000;
            const int iterations = 10;

            int messageCounter = 0;
            double messageParseTimeTotal = 0;
            var stopwatch = new Stopwatch();

            var messageProvider = new MessageProvider<int, IStagedBodyMessage>();

            var readStream = await GetReadStreamAsync(messageCount).ConfigureAwait(false);
            var writeStream = new MemoryStream((int) readStream.Length);

            var messageStream = new MessageStream<IStagedBodyMessage>(
                    new StagedBodyMessageDeserializer(
                        messageProvider,
                        new TestMessageDeserializer()
                    ),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer(),
                        new TestMessage2Serializer()
                    ),
                    new StreamDuplexMessageStream(readStream, writeStream)
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

            Console.ReadLine();
        }

        private static async Task<Stream> GetReadStreamAsync(int messageCount)
        {
            var memoryStream = new MemoryStream();
            var messageStream = new MessageStream<IStagedBodyMessage>(
                           new StagedBodyMessageDeserializer(
                               new MessageProvider<int, IStagedBodyMessage>(),
                               new TestMessageDeserializer()
                           ),
                           new StagedBodyMessageSerializer(
                               new TestMessageSerializer(),
                               new TestMessage2Serializer()
                           ),
                           new StreamDuplexMessageStream(new MemoryStream(), memoryStream)
                       );
            var stopwatch = new Stopwatch();

            await messageStream.OpenAsync().ConfigureAwait(false);

            stopwatch.Start();

            var random = new Random();
            for (int i = 0; i < messageCount; i++)
            {
                if (i % 2 == 0)
                {
                    await messageStream.WriteAsync(new TestMessage
                    {
                        Value = (short)random.Next(short.MaxValue)
                    }).ConfigureAwait(false);
                }
                else
                {
                    await messageStream.WriteAsync(new TestMessage2
                    {
                        Value = (uint) random.Next(int.MaxValue)
                    }).ConfigureAwait(false);
                }

                if (i % 1000 == 0)
                {
                    await messageStream.FlushAsync().ConfigureAwait(false);
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Took {stopwatch.ElapsedMilliseconds}ms to write {messageCount} messages. {messageCount / stopwatch.Elapsed.TotalSeconds} messages/s");

            await messageStream.CloseAsync().ConfigureAwait(false);

            memoryStream.Position = 0;

            return memoryStream;
        }

        public static void Bullshit()
        {
            IMessageBodyDeserializer<int, StagedBodyMessageHeader, TestMessage> deser = new TestMessageDeserializer();

            var readOnlyBuffer = new ReadOnlySpan<byte>();
            var header = new StagedBodyMessageHeader();
            var message = new TestMessage();

            deser.DeserializeOnto(readOnlyBuffer, header, ref message);
        }

        public static void Bullshit2()
        {
            TestMessage2Deserializer deser = new TestMessage2Deserializer();

            var readOnlyBuffer = new ReadOnlySpan<byte>();
            var header = new StagedBodyMessageHeader();
            var message = new TestMessage2();

            deser.DeserializeOnto(readOnlyBuffer, header, ref message);
        }

    }
}
