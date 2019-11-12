using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Sandbox.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new NLog.Config.LoggingConfiguration();

            var logbatch = new NLog.Targets.FileTarget("logbatch") { FileName = "log.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logbatch);

            NLog.LogManager.Configuration = config;

            const int port = 4353;

            Console.WriteLine("Press enter to proceed to client connection.");
            Console.ReadLine();

            for (int i = 0; i < 10; i++)
            {
                var clientSandbox = new SocketClientSandbox("127.0.0.1", port);
                var random = new Random();
                Console.WriteLine($"Simulating client {i} with { (((i % 2) == 0) ? "server" : "client") } disconnect test.");
                Task.Run(async () =>
                {
                    await clientSandbox.StartAsync().ConfigureAwait(false);

                    await RunSuccessfulBatchAsync(clientSandbox, random, 40, 100, 100, 200);
                    await RunRandomFailureBatchAsync(clientSandbox, random, 40, 100, 100, 200);
                    await RunSuccessfulBatchAsync(clientSandbox, random, 40, 100, 100, 200);

                    if ((i % 2) == 0)
                    {
                        await RunBatchWithServerDisconnectAsync(clientSandbox, random);
                    }
                    else
                    {
                        await RunBatchWithClientDisconnectAsync(clientSandbox, random);
                    }

                    Console.WriteLine("Press enter to complete.");
                    Console.ReadLine();

                    var result = await clientSandbox.ClientStream.ActiveStream.WriteRequestAsync<SimpleMessage>(new SimpleMessage
                    {
                        Id = 1,
                        Value = 123123,
                        Disconnect = true
                    }, msg => msg.Id == 1, TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    await clientSandbox.StopAsync().ConfigureAwait(false);
                }).Wait();
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();

        }

        private static bool ShouldRetryMessage(SimpleMessage message, MessageWriteRequestResult<SimpleMessage> result, Exception exception)
        {
            bool retry = result.Error || result.Result.Error || result.IsCompleted || result.Result.IsCompleted;
            if (retry)
            {
                // just setting some vars so it doesnt fail again.
                if (message.DontReply)
                {
                    message.DontReply = false;
                }
                if (message.Disconnect)
                {
                    message.Disconnect = false;
                }
                message.Retried = true;
            }
            return retry;
        }

        private static async Task RunSuccessfulBatchAsync(SocketClientSandbox clientSandbox, Random random, int minbatchCount, int batchs, int betMin, int betCount)
        {
            int messageId = 0;
            long time = 0;
            // Simulate success 10 batches
            var successBatches = new List<Task<int[]>>();
            int batchCount = minbatchCount + random.Next(batchs);
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            Console.WriteLine($"Simulating {batchCount} success batches with random amount of messages per batch.");
            for (int i = 0; i < batchCount; i++)
            {
                successBatches.Add(Task.Run(async () =>
                {
                    var msgs = Enumerable.Range(0, betMin + random.Next(betCount)).Select(async _ =>
                    {
                        int thisId = Interlocked.Increment(ref messageId);

                        var start = DateTime.UtcNow;
                        var result = await clientSandbox.ClientStream.WriteRetryableRequestAsync<SimpleMessage>(new SimpleMessage
                        {
                            Id = thisId,
                            Value = random.Next(100)
                        }, ShouldRetryMessage, msg => msg.Id == thisId, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                        //var result = await clientSandbox.ClientStream.ActiveStream.WriteRequestAsync<SimpleMessage>(new SimpleMessage
                        //{
                        //    Id = thisId,
                        //    Value = random.Next(100)
                        //}, msg => msg.Id == thisId, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                        var end = DateTime.UtcNow - start;
                        Interlocked.Add(ref time, (long) end.TotalMilliseconds);
                        if (result.Result.Error)
                        {
                            // Debugger.Break();
                        }

                        return !result.Error && !result.Result.Error && !result.IsCompleted ? 1 : 0;
                    });
                    return await Task.WhenAll(msgs).ConfigureAwait(false);
                }));
            }
            var successCount = (await Task.WhenAll(successBatches).ConfigureAwait(false)).SelectMany(i => i).Sum(i => i);
            stopwatch.Stop();
            var timePerReq = (time / messageId);
            if (successCount != messageId)
            {
                Console.WriteLine($"Should have received {messageId} success messages but received {successCount}");
                Console.ReadLine();
                Debugger.Break();
            }
            else
            {
                Console.WriteLine($"Success. Took " + stopwatch.ElapsedMilliseconds);
            }
        }

        private static async Task RunRandomFailureBatchAsync(SocketClientSandbox clientSandbox, Random random, int minbatchCount, int batchs, int betMin, int betCount)
        {
            int messageId = 0;
            int expectedRetries = 0;
            int retries = 0;
            int batchCount = minbatchCount + random.Next(batchs);
            // Simulate random no reply 10 batches
            var randomFailureBatches = new List<Task<int[]>>();
            Console.WriteLine($"Simulating {batchCount} batches with random amount of messages per batch. A random amount of messages will be failed and retried.");
            for (int i = 0; i < batchCount; i++)
            {
                randomFailureBatches.Add(Task.Run(async () =>
                {
                    var msgs = Enumerable.Range(0, 200 + random.Next(200)).Select(async _ =>
                    {
                        int thisId = Interlocked.Increment(ref messageId);
                        bool dontReply = random.Next(10) <= 5;
                        if (dontReply)
                        {
                            Interlocked.Increment(ref expectedRetries);
                        }
                        var message = new SimpleMessage
                        {
                            Id = thisId,
                            Value = random.Next(100),
                            DontReply = dontReply
                        };
                        var result = await clientSandbox.ClientStream.WriteRetryableRequestAsync<SimpleMessage>(message, ShouldRetryMessage, msg => msg.Id == thisId, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                        if (message.Retried)
                        {
                            Interlocked.Increment(ref retries);
                        }
                        return !result.Error && !result.Result.Error ? 1 : 0;
                    });
                    return await Task.WhenAll(msgs).ConfigureAwait(false);
                }));
            }

            var failResults = await Task.WhenAll(randomFailureBatches).ConfigureAwait(false);
            int successCount = failResults.SelectMany(i => i).Sum(i => i);
            var failedCount = failResults.SelectMany(i => i).Where(i => i == 0).Count();
            if (failedCount > 0)
            {
                Console.WriteLine($"Should have received 0 failures but received {failedCount} failed and {successCount} success.");
                Console.ReadLine();
                Debugger.Break();
            }
            else
            {
                Console.WriteLine($"Retry count success.");
            }
            if (retries != expectedRetries)
            {
                Console.WriteLine($"Should have received {expectedRetries} retries messages but received {retries}");
                Console.ReadLine();
                Debugger.Break();
            }
            else
            {
                Console.WriteLine($"Retry count success.");
            }
            if (successCount != messageId)
            {
                Console.WriteLine($"Should have received {messageId} success messages but received {successCount}");
                Console.ReadLine();
                Debugger.Break();
            }
            else
            {
                Console.WriteLine($"Success count success.");
            }
        }

        private static async Task RunBatchWithServerDisconnectAsync(SocketClientSandbox clientSandbox, Random random)
        {
            int messageId = 0;
            int expectedSuccess = 0;
            // Simulate random no reply 10 batches
            var randomFailureBatches = new List<Task<MessageWriteRequestResult<SimpleMessage>[]>>();
            Console.WriteLine("Simulating 10 batches with random amount of messages per batch.The first batch will disconnect us from the server and the rest should fail as well.");
            for (int i = 0; i < 10; i++)
            {
                randomFailureBatches.Add(Task.Run(async () =>
                {
                    var msgs = Enumerable.Range(0, 1 + random.Next(100)).Select(async _ =>
                    {
                        int thisId = Interlocked.Increment(ref messageId);
                        Interlocked.Increment(ref expectedSuccess);
                        var result = await clientSandbox.ClientStream.WriteRetryableRequestAsync<SimpleMessage>(new SimpleMessage
                        {
                            Id = thisId,
                            Value = random.Next(100),
                            Disconnect = thisId == 1
                        }, ShouldRetryMessage, msg => msg.Id == thisId, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                        return result;
                    });
                    return await Task.WhenAll(msgs).ConfigureAwait(false);
                }));
            }

            var failResults = await Task.WhenAll(randomFailureBatches).ConfigureAwait(false);
            int successCount = failResults.SelectMany(i => i).Where(r => !r.Error && !r.Result.Error).Count();
            var failedList = failResults.SelectMany(i => i).Where(r => r.Error || r.Result.Error).ToList();
            var failedCount = failedList.Count;
            if (failedCount > 0)
            {
                Console.WriteLine($"Should have received 0 failed messages but received {failedCount}");
                Console.ReadLine();
                Debugger.Break();
            }
            if (successCount != expectedSuccess)
            {
                Console.WriteLine($"Should have received {expectedSuccess} success messages but received {successCount}");
                Console.ReadLine();
                Debugger.Break();
            }
        }

        private static async Task RunBatchWithClientDisconnectAsync(SocketClientSandbox clientSandbox, Random random)
        {
            int messageId = 0;
            int expectedFails = 0;
            // Simulate random no reply 10 batches
            var randomFailureBatches = new List<Task<int[]>>();
            Console.WriteLine("Simulating 10 batches with random amount of messages per batch.The first batch will disconnect us from the server and the rest should fail as well.");
            for (int i = 0; i < 10; i++)
            {
                randomFailureBatches.Add(Task.Run(async () =>
                {
                    var msgs = Enumerable.Range(0, 200 + random.Next(200)).Select(async _ =>
                    {
                        int thisId = Interlocked.Increment(ref messageId);
                        Interlocked.Increment(ref expectedFails);

                        if (thisId == 1)
                        {
                            var ignored = clientSandbox.ClientStream.CloseAsync();
                        }

                        var result = await clientSandbox.ClientStream.WriteRetryableRequestAsync<SimpleMessage>(new SimpleMessage
                        {
                            Id = thisId,
                            Value = random.Next(100),
                            Disconnect = thisId == 1
                        }, ShouldRetryMessage, msg => msg.Id == thisId, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                        return !result.Error && !result.Result.Error && !result.IsCompleted ? 1 : 0;
                    });
                    return await Task.WhenAll(msgs).ConfigureAwait(false);
                }));
            }

            // some will get through which is no big deal.
            var failResults = await Task.WhenAll(randomFailureBatches).ConfigureAwait(false);
            var failedCount = failResults.SelectMany(i => i).Where(i => i == 0).Count();
            if (failedCount == 0)
            {
                Console.WriteLine($"Should have received more than one failure. Received {failedCount}");
                Console.ReadLine();
                Debugger.Break();
            }
            else
            {
                Console.WriteLine($"Success.");
            }
        }

    }
}
