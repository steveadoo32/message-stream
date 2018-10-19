using ProtoBuf;

namespace MessageStream.Sockets.Tests
{
    [ProtoContract]
    public class PongMessage
    {

        [ProtoMember(1)]
        public short Id { get; set; }

    }
}
