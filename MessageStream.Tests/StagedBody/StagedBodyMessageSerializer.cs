using MessageStream.Serializer;
using System;
using System.Collections.Generic;
using System.Text;

namespace MessageStream.Tests.StagedBody
{

    /// <summary>
    /// TODO build out the serialization to work the same way as deserialization
    /// </summary>
    public class StagedBodyMessageSerializer : ISerializer<IStagedBodyMessage>
    {

        public Span<byte> Serialize(IStagedBodyMessage message, in Span<byte> buffer = default, bool bufferProvided = false)
        {
            int index = 0;

            // Test only has one message type atm
            buffer.WriteShort(ref index, message.Header.MessageId);
            buffer.WriteShort(ref index, message.Header.MessageBodyLength);

            var testMessage = message as TestMessage;
            buffer.WriteShort(ref index, testMessage.Value);

            return buffer;
        }

        public bool TryCalculateMessageSize(IStagedBodyMessage message, out int size)
        {
            size = 6;
            return true;
        }

    }

}
