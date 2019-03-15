using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Sandbox.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new NLog.Config.LoggingConfiguration();

            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "file.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;

            const int port = 4353;

            var clients = new List<SocketClientSandbox>();

            Task.Run(async () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var clientSandbox = new SocketClientSandbox("127.0.0.1", port);
                    clients.Add(clientSandbox);
                    await clientSandbox.StartAsync().ConfigureAwait(false);
                }

                await Task.WhenAll(clients.Select(c => c.SendMessagesAsync()).ToList()).ConfigureAwait(false);
            });

            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();

            Task.WhenAll(clients.Select(client => client.StopAsync())).Wait();

        }
    }
}
