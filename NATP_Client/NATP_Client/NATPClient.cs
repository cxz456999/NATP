
using NATP.Signaling;
using NATP.STUN;
using NetCoreServer;
using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NATP
{
    public class NATPClient
    {
        public NATP_STUNClient Stun => stunClient;
        public NATP_SignalingClient Sig => sigClient;
        private NATP_SignalingClient sigClient;
        private NATP_STUNClient stunClient;
        private string signalingServerIP;
        private int signalingServerPort;
        private string stunServerIP;
        private int stunServerPort;

        private string roomName = "Default";
        private string roomDescription = "";

        private bool IsServer = false;
        private bool IsClient = false;

        public bool IsServerActive => IsServer && sigClient != null && stunClient != null && sigClient.IsConnected && stunClient.IsConnected;
        public bool IsConnected => IsClient && Stun != null && Stun.IsConnected;// && IsServer && sigClient != null && stunClient != null && sigClient.IsConnected && stunClient.IsConnected;
        public NATPClient(string sip, int sp, string pfxPath, string stip, int stp, bool _isServer)
        {
            IsServer = _isServer;
            signalingServerIP = sip;
            signalingServerPort = sp;
            stunServerIP = stip;
            stunServerPort = stp;
            stunClient = new NATP_STUNClient(stunServerIP, stunServerPort, _isServer);
            var context = new SslContext(SslProtocols.Tls12, new X509Certificate2(pfxPath, "natp"), (sender, certificate, chain, sslPolicyErrors) => true);
            sigClient = new NATP_SignalingClient(context, signalingServerIP, signalingServerPort);
            IsServer = _isServer;
            IsClient = !_isServer;
        }
        ~NATPClient()
        {
            Disconnect();
            sigClient = null;
            stunClient = null;
        }

        public void Disconnect()
        {
            if (sigClient != null) sigClient.DisconnectAndStop();
            if (stunClient != null) stunClient.DisconnectAndStop();
        }
        public void StartHost(string name, string description="")
        {
            if (IsClient) return;
            if (name.Length <= 0) return;
            roomDescription = description;
            roomName = name;

            stunClient.Core.OnAllocateResponseEvent -= OnAllocateResponseEvent;
            stunClient.Core.OnAllocateResponseEvent += OnAllocateResponseEvent;


            sigClient.OnConnectedEvent -= TriggerServerOnConnectedEventOnce;
            sigClient.OnConnectedEvent += TriggerServerOnConnectedEventOnce;

            sigClient.Core.OnCreateRoomResponseEvent -= OnCreateRoomResponse;
            sigClient.Core.OnCreateRoomResponseEvent += OnCreateRoomResponse;

            sigClient.Core.OnConnectionAttemptResponseEvent -= OnConnectionAttemptResponse;
            sigClient.Core.OnConnectionAttemptResponseEvent += OnConnectionAttemptResponse;

            sigClient.ConnectAsync();
        }

        #region Client
        public void GetRoomList()
        {
            if (IsServer) return;
            sigClient.OnConnectedEvent += GetRoomList_TriggerClientOnConnectedEventOnce;
            if (!sigClient.IsConnected) ClientConnectSignalingServer();
            else sigClient.Core.GetRoomList();
        }
        public void ClientConnect(string ip, int port) // for client
        {
            if (IsServer) return;

            // for client, stunServerIP&Port are Host IP&Port
            stunServerIP = ip;
            stunServerPort = port;

            sigClient.OnConnectedEvent += TriggerClientOnConnectedEventOnce;

            if (!sigClient.IsConnected) ClientConnectSignalingServer();
            else sigClient.JoinRoom(new IPEndPoint(IPAddress.Parse(stunServerIP), stunServerPort));
            stunClient.Connect(stunServerIP, stunServerPort);
        }
        public bool ClientSend(byte[] data) // for client
        {
            if (IsServer) return false;
            stunClient.Send(data);
            return true;
        }
        public bool ClientSend(byte[] data, int offset, int size) // for client
        {
            if (IsServer) return false;
            stunClient.Send(data, offset, size);
            return true;
        }
        private void ClientConnectSignalingServer()
        {

            sigClient.Core.OnJoinRoomResponseEvent -= OnJoinRoomResponseEvent;
            sigClient.Core.OnJoinRoomResponseEvent += OnJoinRoomResponseEvent;

            sigClient.ConnectAsync();
        }
        #endregion
        #region Server
        public bool ServerSend(int connectionId, byte[] data, int offset, int size) // for server
        {
            if (IsClient) return false;
            stunClient.TranslateSend(connectionId, data, offset, size);
            return true;
        }
        #endregion

        #region Events
        public void OnCreateRoomResponse(object sender, NATP_SignalingEventArgs args)
        {
            if (!args.Status) Disconnect();
        }
        private void OnConnectionAttemptResponse(object sender, NATP_SignalingEventArgs args)
        {
            if (args.ipEndPoint == null)
            {
                // OnConnectionAttemptResponse Failed;
            }
            else
            {
                stunClient.Core.AddPeerAndBind(args.ipEndPoint);
            }

        }
        private void OnAllocateResponseEvent(object sender, NATP_SignalingEventArgs args)
        {
            if (args.ipEndPoint == null)
            {
                // OnAllocateResponseEvent Failed;
            }
            else
            {
                
                sigClient.CreateRoom(args.ipEndPoint, roomName, roomDescription);
            }
        }
        private void OnJoinRoomResponseEvent(object sender, NATP_SignalingEventArgs args)
        {
            
            if (args.Status) stunClient.Connect(stunServerIP, stunServerPort);
        }

        private void GetRoomList_TriggerClientOnConnectedEventOnce(object sender, EventArgs args)
        {
            sigClient.OnConnectedEvent -= GetRoomList_TriggerClientOnConnectedEventOnce;
            // for client, stunServerIP&Port are Host IP&Port
            sigClient.Core.GetRoomList();
        }
        private void TriggerClientOnConnectedEventOnce(object sender, EventArgs args)
        {
            sigClient.OnConnectedEvent -= TriggerClientOnConnectedEventOnce;
            // for client, stunServerIP&Port are Host IP&Port
            sigClient.JoinRoom(new IPEndPoint(IPAddress.Parse(stunServerIP), stunServerPort));
        }
        private void TriggerServerOnConnectedEventOnce(object sender, EventArgs args)
        {

            sigClient.OnConnectedEvent -= TriggerServerOnConnectedEventOnce;
            

           stunClient.Connect();
        }
        #endregion
    }
}
