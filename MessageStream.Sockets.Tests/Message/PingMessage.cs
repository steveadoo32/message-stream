using ProtoBuf;

namespace MessageStream.Sockets.Tests
{
    [ProtoContract]
    public class PingMessage
    {

        [ProtoMember(1)]
        public short Id { get; set; }

    }
}
