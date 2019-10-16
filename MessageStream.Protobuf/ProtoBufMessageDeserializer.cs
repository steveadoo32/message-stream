using MessageStream.Message;
using System;
using System.IO;

namespace MessageStream.ProtoBuf
{
    public class ProtoBufMessageDeserializer<T> : StagedDeserializer<T>
    {
        
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
        public virtual T ReadBody(in ReadOnlySpan<byte> buffer, ProtoBufMessageHeader header)
        {
            int offset = 0;
            
            var type = Type.GetType(buffer.ReadString(ref offset));

            using (var stream = new MemoryStream(buffer.Slice(offset).ToArray()))
            {
                return (T) global::ProtoBuf.Serializer.Deserialize(type, stream);
            }
        }


    }

    public class ProtoBufMessageDeserializer : ProtoBufMessageDeserializer<object>
    {

    }

}
