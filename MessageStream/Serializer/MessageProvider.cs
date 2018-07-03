using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Serializer
{
    public class MessageProvider<TIdentifier, TMessage> : IMessageProvider<TIdentifier, TMessage>
    {

        T IMessageProvider<TIdentifier, TMessage>.GetMessage<T>(TIdentifier identifier)
        {
            return FastActivator<T>.NewInstance();
        }

    }
}
