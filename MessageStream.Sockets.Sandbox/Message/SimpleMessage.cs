using ProtoBuf;

namespace MessageStream.Sockets.Sandbox
{
    [ProtoContract]
    public class SimpleMessage
    {

        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public int Value { get; set; }

        [ProtoMember(3)]
        public bool DontReply { get; set; }

        [ProtoMember(4)]
        public bool Disconnect { get; set; }

        public bool Retried { get; set; }

    }
}
