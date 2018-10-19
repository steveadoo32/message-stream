using MessageStream.Message;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MessageStream.ProtoBuf
{
    public class ProtoBufMessageSerializer : IMessageSerializer<object>
    {
        public ProtoBufMessageSerializer()
        {
        }
        
        public bool TryCalculateMessageSize(object message, out int size)
        {
            size = -1;
            return false;
        }

        public Span<byte> Serialize(object message, in Span<byte> buffer = default, bool bufferProvided = false)
        {
            using (var memoryStream = new MemoryStream())
            {
                Serializer.Serialize(memoryStream, message);
                var bytes = memoryStream.ToArray();

                int offset = 0;

                string typeName = message.GetType().AssemblyQualifiedName;
                var combinedSpan = new Span<byte>(new byte[4 + typeName.Length + 1 + bytes.Length]);

                combinedSpan.WriteInt(ref offset, combinedSpan.Length - 4);
                combinedSpan.WriteString(ref offset, typeName);

                new Span<byte>(bytes).CopyTo(combinedSpan.Slice(offset));

                return combinedSpan;
            }
        }
        

    }
}
