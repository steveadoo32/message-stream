using MessageStream.Message;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace MessageStream.ProtoBuf
{
    public class ProtoBufMessageDeserializer<T> : StagedDeserializer<T>
    {

        private ConcurrentDictionary<string, Type> TypeNameCache = new ConcurrentDictionary<string, Type>();
        
        public override int HeaderLength => 4;

        [DeserializationStage(0)]
        public ProtoBufMessageHeader ReadHeader(in ReadOnlySpan<byte> headerBuffer, out int bodyLength)
        {
            int offset = 0;

            bodyLength = headerBuffer.ReadInt(ref offset);

            return new ProtoBufMessageHeader
            {
                MessageBodyLength = bodyLength
            };
        }

        [DeserializationStage(1)]
        public T ReadBody(in ReadOnlySpan<byte> buffer, ProtoBufMessageHeader header)
        {
            int offset = 0;
            
            var type = TypeNameCache.GetOrAdd(buffer.ReadString(ref offset), GetTypeByName);

            if (type == null)
            {
                return default;
            }

            using (var stream = new MemoryStream(buffer.Slice(offset).ToArray()))
            {
                return (T) global::ProtoBuf.Serializer.Deserialize(type, stream);
            }
        }

        private static Type GetTypeByName(string typeName)
        {
            return Type.GetType(typeName);
        }

    }

    public class ProtoBufMessageDeserializer : ProtoBufMessageDeserializer<object>
    {

    }

}
