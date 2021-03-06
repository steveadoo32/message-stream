﻿using MessageStream.ProtoBuf;
using MessageStream.Sockets.Server;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MessageStream.Sockets.Sandbox
{
    public class SocketClientSandbox
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static readonly SimpleMessageDeserializer Deserializer = new SimpleMessageDeserializer(false);
        public static readonly SimpleMessageSerializer Serializer = new SimpleMessageSerializer();

        private RecoverableEventMessageStream<SimpleMessage> clientStream;

        public string Ip { get; }

        public int Port { get; }

        public RecoverableEventMessageStream<SimpleMessage> ClientStream => clientStream;

        public SocketClientSandbox(string ip, int port)
        {
            Ip = ip;
            Port = port;
        }

        public async Task StartAsync()
        {
            var config = new SocketConfiguration
            {
                Ip = Ip,
                Port = Port
            };

            clientStream = new RecoverableEventMessageStream<SimpleMessage>(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2),
                3,
                disconnectionEvent => new EventMessageStream<SimpleMessage>(
                    Deserializer,
                    Serializer,
                    new SocketDuplexMessageStreamWrapper(config),
                    HandleClientMessage,
                    HandleClientKeepAlive,
                    disconnectionEvent
                ),
                async stream =>
                {
                    await Task.Delay(10).ConfigureAwait(false);
                },
                stream => new ValueTask(),
                HandleClientDisconnection
            );

            await clientStream.OpenAsync().ConfigureAwait(false);
        }

        public async Task SendMessagesAsync()
        {
            for (int i = 0; i < 1000000; i++)
            {
                await clientStream.ActiveStream.WriteAsync(new SimpleMessage
                {
                    Id = 1,
                    Value = 5
                }).ConfigureAwait(false);
                await Task.Delay(16).ConfigureAwait(false);
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await clientStream.CloseAsync().ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                // can happen is closing an errored stream.
            }
        }

        async ValueTask<bool> HandleClientMessage(SimpleMessage message)
        {
            // Logger.Info($"Client message received: {message}");

            return true;
        }

        ValueTask HandleClientDisconnection(Exception ex)
        {
            Logger.Info($"Client disconnected");
            return new ValueTask();
        }

        ValueTask HandleClientKeepAlive()
        {
            return new ValueTask();
        }

    }
}
