using ProtoBuf;

namespace MessageStream.Tests.Simple
{
    [ProtoContract]
    public class SimpleMessage
    {

        [ProtoMember(1)]
        public short Id { get; set; }

        [ProtoMember(2)]
        public short Value { get; set; }

    }
}
