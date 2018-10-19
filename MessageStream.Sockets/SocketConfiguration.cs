using System.Net.Sockets;

namespace MessageStream.Sockets
{
    public class SocketConfiguration
    {

        public string Ip { get; set; }

        public int Port { get; set; }

        public AddressFamily AddressFamily { get; set; } = AddressFamily.InterNetwork;

        public SocketType SocketType { get; set; } = SocketType.Stream;

        public ProtocolType ProtocolType { get; set; } = ProtocolType.Tcp;

    }
}