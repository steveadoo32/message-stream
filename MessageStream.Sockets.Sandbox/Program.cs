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
            const int port = 9031;

            var serverSandbox = new SocketServerSandbox(port);
            Task.Run(async () =>
            {
                try
                {
                    await serverSandbox.StartAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            });

            Console.WriteLine("Server started. Press enter to quit.");
            Console.ReadLine();

            Console.WriteLine("Quitting server.");

            Task.Run(async () =>
            {
                await serverSandbox.StopAsync().ConfigureAwait(false);
            }).Wait();

        }

    }
}
