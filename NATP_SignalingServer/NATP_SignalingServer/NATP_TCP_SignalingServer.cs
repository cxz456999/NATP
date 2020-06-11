using NetCoreServer;
using System;
using System.Net;
using System.Net.Sockets;

namespace NATP.Signaling.Server
{
    public class NATP_TCP_SignalingSession : TcpSession, INATP_SignalingServerSender
    {
        private NATP_SignalingServerCore sigCore;
        public NATP_TCP_SignalingSession(TcpServer server) : base(server) { sigCore = new NATP_SignalingServerCore(this); }

        protected override void OnConnected()
        {
            NATP_OnConnected();
        }

        protected override void OnDisconnected()
        {
            Console.WriteLine($"Chat TCP session with Id {Id} disconnected!");
            sigCore.OnDisconnected();
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            sigCore.OnResponse(buffer, offset, size);
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP session caught an error with code {error}");
        }

        public void NATP_OnConnected()
        {
            Console.WriteLine("IP " + IPAddress.Parse(((IPEndPoint)Socket.RemoteEndPoint).Address.ToString()) + " on port number " + ((IPEndPoint)Socket.RemoteEndPoint).Port.ToString() + " connected!");
            sigCore.RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        }
    }
    public class NATP_TCP_SignalingServer : TcpServer
    {
        public NATP_TCP_SignalingServer(IPAddress address, int port) : base(address, port) { }

        protected override TcpSession CreateSession() { return new NATP_TCP_SignalingSession(this); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP server caught an error with code {error}");
        }
    }
}
