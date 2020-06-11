using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NATP.Signaling
{
    public class NATP_SignalingEventArgs : EventArgs
    {
        public bool Status;
        public string Message;
        public IPEndPoint ipEndPoint;
        public List<Room> roomList;
        public NATP_SignalingEventArgs(bool s, string m, IPEndPoint ip) => (Status, Message, ipEndPoint) = (s, m, ip);
        public NATP_SignalingEventArgs(bool s, List<Room> r) => (Status, roomList) = (s, r);
        public NATP_SignalingEventArgs(bool s, string m) => (Status, Message) = (s, m);
        public NATP_SignalingEventArgs(bool s) => (Status) = (s);
    }

    public class NATP_SignalingClientCore
    {
        public event EventHandler<NATP_SignalingEventArgs> OnJoinRoomResponseEvent;
        public event EventHandler<NATP_SignalingEventArgs> OnCreateRoomResponseEvent;
        public event EventHandler<NATP_SignalingEventArgs> OnConnectionAttemptResponseEvent;
        public event EventHandler<NATP_SignalingEventArgs> OnGetRoomListResponseEvent;

        public static string RoomTag = "game";
        private Dictionary<string, IPEndPoint> room = new Dictionary<string, IPEndPoint>();
        //private string externalIP = "";
        //private ushort externalPort = 1122;

        private INATP_SignalingClient sender;
        public NATP_SignalingClientCore(INATP_SignalingClient _sender)
        {
            this.sender = _sender;
            //GetPublicIP();
        }

        public void OnResponse(byte[] buffer, long offset, long size)
        {
            SignalingClientMessage ssM = new SignalingClientMessage();
            if (!ssM.FromBuffer(buffer, offset, size)) return;
            bool status;
            switch (ssM.methodType)
            {
                case SignalingMethod.CreateRoomResponse:
                    status = ssM.Get(SignalingAttribute.Success) == null ? false : true;
                    OnCreateRoomResponseEvent?.Invoke(this, new NATP_SignalingEventArgs(status, ""));
                    break;
                case SignalingMethod.JoinRoomResponse:
                    status = ssM.Get(SignalingAttribute.Success) == null ? false : true;
                    OnJoinRoomResponseEvent?.Invoke(this, new NATP_SignalingEventArgs(status, ""));
                    break;
                case SignalingMethod.CloseRoomResponse:
                    break;
                case SignalingMethod.ConnectionAttemptResponse:
                    OnConnectionAttemptResponse(ssM);
                    break;
                case SignalingMethod.GetRoomListResponse:
                    OnGetRoomListResponse(ssM);
                    break;
            }
        }

        #region Request
        public void CreateRoom(IPEndPoint ipe, string roomName, string description)
        {
            SignalingClientMessage ssm = new SignalingClientMessage(SignalingMethod.CreateRoomRequest);
            byte[] addressByte = ipe.Address.GetAddressBytes();
            byte[] ip = new byte[3 + addressByte.Length];
            if (addressByte.Length > 4) ip[0] = 0x2;
            else ip[0] = 0x1;
            ushort port = (ushort)ipe.Port;
            ip[2] = (byte)(port & 0xff);
            ip[1] = (byte)((port >> 8) & 0xff);
            int idx = 3;
            for (int i = addressByte.Length - 1; i >= 0; i--)
                ip[idx++] = addressByte[i];
            //Array.Copy(addressByte, 0, ip, 3, addressByte.Length);
            ssm.WriteBytes(SignalingAttribute.RoomAddress, ip);
            ssm.WriteString(SignalingAttribute.RoomTag, RoomTag);
            ssm.WriteString(SignalingAttribute.RoomName, roomName);
            if (description == null || description.Length == 0)
                ssm.WriteEmpty(SignalingAttribute.RoomDescription);
            else
                ssm.WriteString(SignalingAttribute.RoomDescription, description);
            
            sender.Send(ssm.WriteRequest());
        }
        public void JoinRoom(IPEndPoint ipe)
        {
            //if (externalIP.Length == 0) throw new Exception("Can't Get Public Ip address.");
            SignalingClientMessage ssm = new SignalingClientMessage(SignalingMethod.JoinRoomRequest);

            byte[] addressByte = ipe.Address.GetAddressBytes();
            byte[] ip = new byte[3 + addressByte.Length];
            if (addressByte.Length > 4) ip[0] = 0x2;
            else ip[0] = 0x1;
            ushort port = (ushort)ipe.Port;
            ip[2] = (byte)(port & 0xff);
            ip[1] = (byte)((port >> 8) & 0xff);
            int idx = 3;
            for (int i = addressByte.Length - 1; i >= 0; i--)
                ip[idx++] = addressByte[i];
            //Array.Copy(addressByte, 0, ip, 3, addressByte.Length);
            ssm.WriteBytes(SignalingAttribute.RoomAddress, ip);
            ssm.WriteString(SignalingAttribute.RoomTag, RoomTag);
            /*addressByte = IPString2Bytes(externalIP);
            ip = new byte[3 + addressByte.Length];
            if (addressByte.Length > 4) ip[0] = 0x2;
            else ip[0] = 0x1;
            port = (ushort)externalPort;
            ip[2] = (byte)(port & 0xff);
            ip[1] = (byte)((port >> 8) & 0xff);
            //Array.Copy(addressByte2, 0, ip2, 3, addressByte2.Length);
            idx = 3;
            for (int i = addressByte.Length - 1; i >= 0; i--)
                ip[idx++] = addressByte[i];
            ssm.WriteBytes(SignalingAttribute.PeerAddress, ip);*/

            sender.Send(ssm.WriteRequest());
        }
        public void GetRoomList()
        {
            SignalingClientMessage ssm = new SignalingClientMessage(SignalingMethod.GetRoomListRequest);
            ssm.WriteString(SignalingAttribute.RoomTag, RoomTag);
            sender.Send(ssm.WriteRequest());
        }
        #endregion
        #region On Events
        private void OnConnectionAttemptResponse(SignalingClientMessage ssM)
        {
            IPEndPoint peer = (IPEndPoint)ssM.Get(SignalingAttribute.PeerAddress);
            OnConnectionAttemptResponseEvent?.Invoke(this, new NATP_SignalingEventArgs(true, "", peer));
        }
        private void OnGetRoomListResponse(SignalingClientMessage ssM)
        {
            //List<IPEndPoint> roomlist = (List<IPEndPoint>)ssM.Get(SignalingAttribute.RoomAddress);
            List<Room> roomList = (List<Room>)ssM.Get(SignalingAttribute.Room);

            OnGetRoomListResponseEvent?.Invoke(this, new NATP_SignalingEventArgs(true, roomList));
        }
        #endregion
        #region Uitilities
        public static byte[] IPString2Bytes(string ip)
        {
            byte[] address = BitConverter.GetBytes(stringToHexIP(ip));
            Array.Reverse(address);
            return address;
        }
        public static uint stringToHexIP(string strIP)
        {
            uint ip = 0;
            string[] ipseg = strIP.Split('.');
            for (int i = 0; i < ipseg.Length; i++)
            {
                uint first = uint.Parse(ipseg[i]);
                string hexValue = first.ToString("X");
                uint uintAgain = uint.Parse(hexValue, System.Globalization.NumberStyles.HexNumber);
                ip = (ip << 8) + uintAgain;
            }
            return ip;
        }
        /*void GetPublicIP()
        {
            try
            {
                externalIP = new WebClient().DownloadString("https://api.ipify.org/");
            }
            catch (Exception e)
            {
                try
                {
                    externalIP = new WebClient().DownloadString("http://checkip.dyndns.org/");
                    int start = externalIP.IndexOf("Current IP Address: ") + "Current IP Address: ".Length;
                    externalIP = externalIP.Substring(start, externalIP.IndexOf("</body>") - start);
                }
                catch (Exception e2)
                {

                    try
                    {
                        
                    }
                    catch (Exception e3)
                    {

                    }
                }
            }
        }*/
        #endregion
    }
}