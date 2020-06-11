﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
namespace NATP.Signaling.Server
{
    class NATP_SignalingServerCore
    {
        private static Dictionary<string, INATP_SignalingServerSender> senderTable = new Dictionary<string, INATP_SignalingServerSender>();
        private static List<Room> room = new List<Room>();

        private static object _lock = new object();
        private INATP_SignalingServerSender sender;
        private string clientKey;
        public IPEndPoint RemoteEndPoint;
        
        public NATP_SignalingServerCore(INATP_SignalingServerSender sender)
        {
            this.sender = sender;
        }

        #region API
        public void OnResponse(byte[] buffer, long offset, long size)
        {
            SignalingServerMessage ssM = new SignalingServerMessage();
            if (!ssM.FromBuffer(buffer, offset, size)) return;
            Console.WriteLine("\nIncomming Request: {0}", Enum.GetName(typeof(SignalingMethod), ssM.methodType));
            switch (ssM.methodType)
            {
                case SignalingMethod.CreateRoomRequest:
                    OnCreateRoom(ssM);
                    break;
                case SignalingMethod.CloseRoomRequest:
                    OnCloseRoom(ssM);
                    break;
                case SignalingMethod.JoinRoomRequest:
                    OnJoinRoom(ssM);
                    break;
                case SignalingMethod.GetRoomListRequest:
                    OnGetRoomList(ssM);
                    break;
            }
        }
        public void OnDisconnected()
        {
            if (clientKey!=null)CloseRoom(clientKey);
        }
        #endregion
        delegate bool ser(Room x);
        private bool CloseRoom(string key)
        {     
            lock (_lock)
            {
                int idx = room.FindIndex(x => x.Key == key);
                if (idx >= 0)
                {
                    Console.WriteLine("Close Room: '{0}'", room[idx].ToString());
                    room.RemoveAt(idx);
                    if (senderTable.ContainsKey(key)) senderTable.Remove(key);
                }
                else return false;
            }
            
            return true;
        }
        #region On Events
        private void OnCreateRoom(SignalingServerMessage ssm) 
        {
            IPEndPoint ipe = (IPEndPoint)ssm.Get(SignalingAttribute.RoomAddress);
            string tag = (string)ssm.Get(SignalingAttribute.RoomTag);
            string name = (string)ssm.Get(SignalingAttribute.RoomName);
            string des = (string)ssm.Get(SignalingAttribute.RoomDescription);
            string key = ipe.ToString();
            if (tag.Length <= 0)
            {
                Console.WriteLine("Create Room: '[{0}]{1} Failed!'", tag, key);
                ResponseCreateRoom(false);
            }
            lock (_lock)
            {
                int idx = room.FindIndex(x => x.Key == key);
                
                if (idx != -1)
                {
                    Console.WriteLine("Create Room: '[{0}]{1} Failed!'", tag, key);
                    //room[idx].Update(tag);
                    ResponseCreateRoom(false);
                    
                }
                else
                {
                    Room r = new Room(tag, ipe, des, name);
                    room.Add(r);
                    senderTable.Add(key, sender);
                    clientKey = key;
                    
                    ResponseCreateRoom(true);
                    Console.WriteLine("Create Room: " + r.ToString());
                }
            }
        }
        private void OnCloseRoom(SignalingServerMessage ssm) 
        {
            IPEndPoint ipe = (IPEndPoint)ssm.Get(SignalingAttribute.RoomAddress);
            CloseRoom(ipe.ToString());
        }
        private void OnJoinRoom(SignalingServerMessage ssm) 
        {
            IPEndPoint roomName = (IPEndPoint)ssm.Get(SignalingAttribute.RoomAddress);
            string tag = (string)ssm.Get(SignalingAttribute.RoomTag);
            string key = roomName.ToString();
            lock (_lock)
            {
                Room target = room.Find(x => x.Key == key && x.Tag == tag);
                if (target != null)
                {
                    if (senderTable.ContainsKey(key))
                    {
                        ResponseJoinRoom(true);
                        ResponseConnectionAttemptRequest(key, RemoteEndPoint);
                    }
                    else
                        ResponseJoinRoom(false);
                    Console.WriteLine("Peer '{0}' Join Room: '{1}'", RemoteEndPoint.ToString(), key);
                }
                else
                    Console.WriteLine("Peer '{0}' Join Room: '{1}' Failed", RemoteEndPoint.ToString(), key);
            }
        }
        private void OnGetRoomList(SignalingServerMessage ssm)
        {
            ResponseGetRoomList((string)ssm.Get(SignalingAttribute.RoomTag));
        }
        private void OnLeaveRoom(SignalingServerMessage ssm) 
        {

        }
        #endregion
        #region Write
        private void ResponseCreateRoom(bool success)
        {
            SignalingServerMessage ssm = new SignalingServerMessage(SignalingMethod.CreateRoomResponse);
            if (success) ssm.WriteEmpty(SignalingAttribute.Success);
            else ssm.WriteEmpty(SignalingAttribute.Failed);
            sender.Send(ssm.WriteRequest());
        }
        private void ResponseJoinRoom(bool success)
        {
            SignalingServerMessage ssm = new SignalingServerMessage(SignalingMethod.JoinRoomResponse);
            if (success) ssm.WriteEmpty(SignalingAttribute.Success);
            else ssm.WriteEmpty(SignalingAttribute.Failed);
            sender.Send(ssm.WriteRequest());
            //sender.Disconnect();
        }
        private void ResponseGetRoomList(string tag)
        {
            SignalingServerMessage ssm = new SignalingServerMessage(SignalingMethod.GetRoomListResponse);
            //
            List<Room> sameTag;
            lock (_lock)
            {
                sameTag = room.FindAll(x => x.Tag == tag);
            }
            //Console.WriteLine("Result Room Tag: {0}, total: {1}", tag, sameTag.Count);
            for (int i = 0; i < sameTag.Count; i++)
            {
                Console.WriteLine("Find: " + sameTag[i].ToString());
                byte[] addressByte = sameTag[i].IP.Address.GetAddressBytes();
                byte[] ip = new byte[3 + addressByte.Length];
                if (addressByte.Length > 4) ip[0] = 0x2;
                else ip[0] = 0x1;
                ushort port = (ushort)sameTag[i].IP.Port;
                ip[2] = (byte)(port & 0xff);
                ip[1] = (byte)((port >> 8) & 0xff);
                Array.Copy(addressByte, 0, ip, 3, addressByte.Length);
                ssm.WriteString(SignalingAttribute.RoomName, sameTag[i].Name);
                ssm.WriteString(SignalingAttribute.RoomDescription, sameTag[i].Description);
                ssm.WriteBytes(SignalingAttribute.RoomAddress, ip);
                Console.WriteLine("Key: " + sameTag[i].Key);
            }
            Console.WriteLine("Result Room Tag: {0}, total: {1}", tag, sameTag.Count);
            sender.Send(ssm.WriteRequest());
        }
        private void ResponseConnectionAttemptRequest(string key, IPEndPoint ipe)
        {
            SignalingServerMessage ssm = new SignalingServerMessage(SignalingMethod.ConnectionAttemptResponse);
            byte[] addressByte = ipe.Address.GetAddressBytes();
            byte[] ip = new byte[3 + addressByte.Length];
            if (addressByte.Length > 4) ip[0] = 0x2;
            else ip[0] = 0x1;
            ushort port = (ushort)ipe.Port;
            ip[2] = (byte)(port & 0xff);
            ip[1] = (byte)((port >> 8) & 0xff);
            Array.Copy(addressByte, 0, ip, 3, addressByte.Length);
            ssm.WriteBytes(SignalingAttribute.PeerAddress, ip);
            lock (_lock)
            {
                senderTable[key].Send(ssm.WriteRequest());
            }
        }
        #endregion
        #region Utilities
        private static byte[] IPString2Bytes(string ip)
        {
            return BitConverter.GetBytes(stringToHexIP(ip));
        }
        private static uint stringToHexIP(string strIP)
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
        #endregion
    }
}
