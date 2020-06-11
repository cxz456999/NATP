using System;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;

namespace NATP.Signaling.Server
{
    class NATP_SSL_SignalingSession : SslSession, INATP_SignalingServerSender
    {
        private NATP_SignalingServerCore sigCore;
        public NATP_SSL_SignalingSession(SslServer server) : base(server) { sigCore = new NATP_SignalingServerCore(this); }
        protected override void OnConnected()
        {
            NATP_OnConnected();
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


        public void NATP_OnConnected()
        {
            Console.WriteLine("IP " + IPAddress.Parse(((IPEndPoint)Socket.RemoteEndPoint).Address.ToString()) + " on port number " + ((IPEndPoint)Socket.RemoteEndPoint).Port.ToString() + " connected!");
            sigCore.RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        }
    }

    class NATP_SSL_SignalingServer : SslServer
    {
        public NATP_SSL_SignalingServer(SslContext context, IPAddress address, int port) : base(context, address, port) { }

        protected override SslSession CreateSession() { return new NATP_SSL_SignalingSession(this); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat SSL server caught an error with code {error}");
        }
    }
}