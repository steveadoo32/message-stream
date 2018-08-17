namespace MessageStream.Message
{
    public class MessageProvider<TIdentifier, TMessage> : IMessageProvider<TIdentifier, TMessage>
    {

        T IMessageProvider<TIdentifier, TMessage>.GetMessage<T>(TIdentifier identifier)
        {
            return FastActivator<T>.NewInstance();
        }

    }
}
