using System;
using System.Net;
namespace NATP.Signaling
{
    public interface INATP_SignalingClient
    {
        event EventHandler OnConnectedEvent;
        void DisconnectAndStop();
        void CreateRoom(IPEndPoint ipe, string name, string des = "");
        void JoinRoom(IPEndPoint ipe);
        long Send(byte[] buffer);

        void OnJoinRoomResponse(object sender, NATP_SignalingEventArgs args);
        void OnCreateRoomResponse(object sender, NATP_SignalingEventArgs args);
        void OnConnectionAttemptResponse(object sender, NATP_SignalingEventArgs args);
        void OnGetRoomListResponse(object sender, NATP_SignalingEventArgs args);
    }
}
