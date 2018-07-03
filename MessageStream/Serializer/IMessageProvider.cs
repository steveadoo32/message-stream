namespace MessageStream.Serializer
{

    /// <summary>
    /// Provides instances of messages using an identifier
    /// </summary>
    public interface IMessageProvider<TIdentifier, TMessage>
    {

        T GetMessage<T>(TIdentifier identifier) where T : TMessage, new();

    }
}