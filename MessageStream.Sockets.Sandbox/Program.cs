using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Sandbox
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new NLog.Config.LoggingConfiguration();

            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "file.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");
            var logdebug = new NLog.Targets.DebuggerTarget("logdebugger");

            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logdebug);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;

            const int port = 4353;

            var serverSandbox = new SocketServerSandbox(port);

            Task.Run(async () =>
            {
                await serverSandbox.StartAsync().ConfigureAwait(false);
            });

            Console.WriteLine("Server started. Press enter to quit.");
            Console.ReadLine();

            Task.Run(async () =>
            {
                await serverSandbox.StopAsync().ConfigureAwait(false);
            }).Wait();

        }

    }
}
