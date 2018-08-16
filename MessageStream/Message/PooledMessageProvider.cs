using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Message
{
    public class PooledMessageProvider<TIdentifier, TMessage> : IMessageProvider<TIdentifier, TMessage>
    {

        private Dictionary<TIdentifier, ConcurrentQueue<TMessage>> MessageQueues = new Dictionary<TIdentifier, ConcurrentQueue<TMessage>>();

        T IMessageProvider<TIdentifier, TMessage>.GetMessage<T>(TIdentifier identifier)
        {
            var queue = GetQueue(identifier);

            if (queue.TryDequeue(out var message))
            {
                return (T) message;
            }

            return FastActivator<T>.NewInstance();
        }

        public void Return(TIdentifier identifier, TMessage message)
        {
            var queue = GetQueue(identifier);
            queue.Enqueue(message);
        }

        private ConcurrentQueue<TMessage> GetQueue(TIdentifier identifier)
        {
            ConcurrentQueue<TMessage> queue = null;
            if (!MessageQueues.TryGetValue(identifier, out queue))
            {
                lock(MessageQueues)
                {
                    if (!MessageQueues.TryGetValue(identifier, out queue))
                    {
                        MessageQueues.Add(identifier, queue = new ConcurrentQueue<TMessage>());
                    }
                }
            }
            return queue;
        }

    }
}
