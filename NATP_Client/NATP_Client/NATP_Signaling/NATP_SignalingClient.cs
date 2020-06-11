using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using NetCoreServer;

namespace NATP.Signaling
{
    public class NATP_SignalingClient : SslClient, INATP_SignalingClient
    {
        public event EventHandler OnConnectedEvent;
        public NATP_SignalingClientCore Core => sigCore;

        private NATP_SignalingClientCore sigCore;
        public NATP_SignalingClient(SslContext context, string address, int port) : base(context, address, port)
        {
            sigCore = new NATP_SignalingClientCore(this);

            Core.OnConnectionAttemptResponseEvent += OnConnectionAttemptResponse;
            Core.OnCreateRoomResponseEvent += OnCreateRoomResponse;
            Core.OnGetRoomListResponseEvent += OnGetRoomListResponse;
            Core.OnJoinRoomResponseEvent += OnJoinRoomResponse;
        }
        public void CreateRoom(IPEndPoint ipe, string name, string des = "")
        {
            sigCore.CreateRoom(ipe, name, des);
        }
        public void JoinRoom(IPEndPoint ipe)
        {
            
            sigCore.JoinRoom(ipe);
        }

        public void DisconnectAndStop()
        {
            _stop = true;
            DisconnectAsync();
        }
        public void OnGetRoomListResponse(object sender, NATP_SignalingEventArgs args)
        {
            foreach (var r in args.roomList)
            {
                Console.WriteLine(r.ToString());
            }
        }
        public void OnJoinRoomResponse(object sender, NATP_SignalingEventArgs args)
        {

        }

        public void OnCreateRoomResponse(object sender, NATP_SignalingEventArgs args)
        {


        }

        public void OnConnectionAttemptResponse(object sender, NATP_SignalingEventArgs args)
        {
        }

        private bool _stop;

        #region Network
        protected override void OnConnected()
        {
            Console.WriteLine($"Chat SSL client connected a new session with Id {Id}");

        }

        protected override void OnHandshaked()
        {
            Console.WriteLine($"Chat SSL client handshaked a new session with Id {Id}");
            OnConnectedEvent?.Invoke(this, EventArgs.Empty);

        }

        protected override void OnDisconnected()
        {
            Console.WriteLine($"Chat SSL client disconnected a session with Id {Id}");

            // Try to connect again
            if (!_stop)
                ConnectAsync();
        }
        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            Console.WriteLine(Encoding.UTF8.GetString(buffer, (int)offset, (int)size));
            sigCore.OnResponse(buffer, offset, size);
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat SSL client caught an error with code {error}");
        }


        #endregion
    }
}