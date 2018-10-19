using ProtoBuf;

namespace MessageStream.Sockets.Sandbox
{
    [ProtoContract]
    public struct SimpleMessage
    {

        [ProtoMember(1)]
        public short Id { get; set; }

        [ProtoMember(2)]
        public short Value { get; set; }

    }
}
