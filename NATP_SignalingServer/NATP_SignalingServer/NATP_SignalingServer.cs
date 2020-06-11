using System;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;

namespace Signaling.Server
{
    class SignalingSession : SslSession, INATP_SignalingServerSender
    {
        private NATP_SignalingServerCore sigCore;
        public SignalingSession(SslServer server) : base(server) { sigCore = new NATP_SignalingServerCore(this); }
        protected override void OnConnected()
        {
            Console.WriteLine("IP " + IPAddress.Parse(((IPEndPoint)Socket.RemoteEndPoint).Address.ToString()) + " on port number " + ((IPEndPoint)Socket.RemoteEndPoint).Port.ToString() + " connected!");
            sigCore.RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        }

        protected override void OnHandshaked()
        {
            Console.WriteLine($"Chat SSL session with Id {Id} handshaked!");
        }

        protected override void OnDisconnected()
        {
            Console.WriteLine($"Chat SSL session with Id {Id} disconnected!");
            sigCore.OnDisconnected();
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            sigCore.OnResponse(buffer, offset, size);
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat SSL session caught an error with code {error}");
        }
    }

    class NATP_SignalingServer : SslServer
    {
        public NATP_SignalingServer(SslContext context, IPAddress address, int port) : base(context, address, port) { }

        protected override SslSession CreateSession() { return new SignalingSession(this); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat SSL server caught an error with code {error}");
        }
    }
}