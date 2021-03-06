﻿using MessageStream.Benchmark.StagedBody;
using MessageStream.DuplexMessageStream;
using MessageStream.IO;
using MessageStream.Message;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Benchmark
{
    class ProgramConcurrent
    {
        public static async Task Main0(string[] args)
        {
            const int messageCount = 1_000_000;
            const int iterations = 10;
            const int parallellism = 1;

            int messageCounter = 0;
            var stopwatch = new Stopwatch();

            var messageProvider = new MessageProvider<int, IStagedBodyMessage>();

            var readStream = await GetReadStreamAsync(messageCount).ConfigureAwait(false);
            var writeStream = new MemoryStream((int)readStream.Length);

            var messageStream = new ConcurrentMessageStream<IStagedBodyMessage>(
                    new StagedBodyMessageDeserializer(
                        messageProvider,
                        new TestMessageDeserializer(),
                        new TestMessage2Deserializer()
                    ),
                    new StagedBodyMessageSerializer(
                        new TestMessageSerializer(),
                        new TestMessage2Serializer()
                    ),
                    new StreamDuplexMessageStream(readStream, writeStream),
                    exception => new ValueTask()
                );

            Console.WriteLine();

            for (int i = 0; i < iterations; i++)
            {
                readStream.Position = 0;
                writeStream.Position = 0;

                messageCounter = 0;

                await messageStream.OpenAsync().ConfigureAwait(false);

                stopwatch.Restart();

                await Task.WhenAll(Enumerable.Range(0, parallellism).Select(async _ =>
                {
                    while (true)
                    {
                        var result = await messageStream.ReadAsync().ConfigureAwait(false);
                        if (result.ReadResult)
                        {
                            Interlocked.Increment(ref messageCounter);
                        }
                        if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                })).ConfigureAwait(false);

                stopwatch.Stop();

                Console.WriteLine($"Done iteration: {messageCounter / stopwatch.Elapsed.TotalSeconds} messages/s. {messageCounter} total messages read.");

                if (messageStream.Open)
                {
                    await messageStream.CloseAsync().ConfigureAwait(false);
                }
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
                        new TestMessageDeserializer(),
                        new TestMessage2Deserializer()
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
                        Value = (uint)random.Next(int.MaxValue)
                    }).ConfigureAwait(false);
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Took {stopwatch.ElapsedMilliseconds}ms to write {messageCount} messages. {messageCount / stopwatch.Elapsed.TotalSeconds} messages/s");

            await messageStream.CloseAsync().ConfigureAwait(false);

            memoryStream.Position = 0;

            return memoryStream;
        }
    }
}
