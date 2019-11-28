using ProtoBuf;

namespace MessageStream.Sockets.Sandbox
{
    [ProtoContract]
    public class SimpleMessage : IRequest, IResponse
    {

        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public int Value { get; set; }

        [ProtoMember(3)]
        public bool DontReply { get; set; }

        [ProtoMember(4)]
        public bool Disconnect { get; set; }

        [ProtoMember(5)]
        public byte[] Dummy { get; set; }

        public bool Retried { get; set; }

        string IRequest.GetKey()
        {
            return Id.ToString();
        }

        string IResponse.GetKey()
        {
            return Id.ToString();
        }

    }
}
