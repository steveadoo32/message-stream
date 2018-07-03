namespace MessageStream.Tests.StagedBody
{ 

    public struct StagedBodyMessageHeader
    {

        public short MessageId { get; set; }

        public short MessageBodyLength { get; set; }

    }

}