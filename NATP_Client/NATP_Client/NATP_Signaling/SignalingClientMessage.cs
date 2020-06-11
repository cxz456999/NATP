using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace NATP.Signaling
{
    public class Room
    {
        public readonly string Key;
        public string Tag;
        public string Description;
        public string Name;
        public readonly IPEndPoint IP;
        public Room() { }
        public Room(string t, IPEndPoint ip, string des, string name) => (Tag, IP, Key, Description, Name) = (t, ip, ip.ToString(), des, name);
        public void Update(string Tag)
        {
            this.Tag = Tag;
        }
        public override string ToString()
        {
            return "[" + Tag + "]" + Name + "(" + Key + ")\n" + Description + "\n";
        }
    }
    public enum SignalingMethod
    {
        // to signaling server
        CreateRoomRequest = 0x01,
        CloseRoomRequest = 0x02,
        JoinRoomRequest = 0x03,
        GetRoomListRequest = 0x04,
        // to signaling client
        CreateRoomResponse = 0x11,
        CloseRoomResponse = 0x12,
        JoinRoomResponse = 0x13,
        GetRoomListResponse = 0x15,
        ConnectionAttemptResponse = 0x14 // notice room owner someone want to connect to it

    }
    public enum SignalingAttribute
    {
        PeerAddress = 0x01,
        RoomAddress = 0x02,
        RoomName = 0x4,
        RoomDescription = 0x5,
        Room = 0x6,
        RoomTag = 0x03, // distinguish the type of room, any peer want to join this room must have the same roomTag
        Success = 0x11,
        Failed = 0x10,
    }
    /*
     * Header
     * start with a byte 00111000
     * one byte method id
     * one byte attribute id
     * 
     * attribute--
     * PeerAddress: one byte family, two bytes port, (4bytes IPv4/ 16bytes IPv6)
    */
    class SignalingClientMessage
    {
        public bool IsMessage = false;
        public SignalingMethod methodType;

        public static bool CheckMessage(byte b) { return b == StartByte; }

        public static byte StartByte => 0x38;

        public Dictionary<SignalingAttribute, object> response = new Dictionary<SignalingAttribute, object>();
        private NetworkSerializer serializer = new NetworkSerializer(4096);

        public List<SignalingAttribute> attributeTypes = new List<SignalingAttribute>();
        public List<byte[]> attributeBytes = new List<byte[]>();
        public SignalingClientMessage() { }
        public SignalingClientMessage(SignalingMethod m) { methodType = m; }
        public bool FromBuffer(byte[] buffer, long offset, long size)
        {
            if (buffer[offset] != 0x38) return false;
            serializer.SetBuffer(buffer, offset, size);
            serializer.ReadByte(); // read heard 00111000
            methodType = (SignalingMethod)serializer.ReadByte();
            if (!Enum.IsDefined(typeof(SignalingMethod), methodType)) return false;
            //Console.WriteLine("SignalingMethod: {0}", Enum.GetName(typeof(SignalingMethod), methodType));
            ReadAttribute();
            IsMessage = true;
            return true;
        }

        #region Write
        public byte[] WriteRequest()
        {
            Console.WriteLine("\nRequest Method= " + Enum.GetName(typeof(SignalingMethod), methodType));
            serializer.SetBufferLength(0);

            serializer.Write((byte)0x38);

            //method id
            serializer.Write((byte)methodType);

            //attributes
            for (int i = 0; i < attributeBytes.Count; i++)
            {
                //LogAttribute(i);
                serializer.Write(attributeBytes[i]);
            }

            byte[] ret = serializer.ToArray();
            //cleanup
            attributeBytes.Clear();
            serializer.SetBufferLength(0);
            return ret;
        }
        public void WriteBytes(SignalingAttribute attr, byte[] bytes)
        {
            serializer.SetBufferLength(0);
            serializer.Write((byte)attr);
            serializer.Write((ushort)bytes.Length);
            serializer.Write(bytes);

            //pad to multiple of 4
            PadTo32Bits(bytes.Length, serializer);

            Console.WriteLine("Attribute: " + Enum.GetName(typeof(SignalingAttribute), attr) + " = " + NetworkSerializer.ByteArrayToHexString((byte[])bytes));
            response.Add(attr, bytes);
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());

        }

        public void PadTo32Bits(int len, NetworkSerializer serializer)
        {
            while (((len++) % 4) != 0)
                serializer.Write((byte)0);
        }

        public void WriteString(SignalingAttribute attr, string text)
        {
            serializer.SetBufferLength(0);

            int len = Encoding.UTF8.GetByteCount(text);

            serializer.Write((byte)attr);
            serializer.Write(text, len);

            //pad to multiple of 4
            PadTo32Bits(len, serializer);

            response.Add(attr, text);
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteUInt(SignalingAttribute attr, uint value)
        {
            serializer.SetBufferLength(0);

            serializer.Write((byte)attr);
            serializer.Write((ushort)4);
            serializer.Write(value);

            response.Add(attr, value);
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteEmpty(SignalingAttribute attr)
        {
            serializer.SetBufferLength(0);

            serializer.Write((byte)attr);
            serializer.Write((ushort)0);

            response.Add(attr, "");
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }
        #endregion
        #region Read
        public void ReadAttribute()
        {
            List<IPEndPoint> roomAddressList = new List<IPEndPoint>();
            List<string> roomNameList = new List<string>();
            List<string> roomDescriptionList = new List<string>();
            List<Room> roomList = new List<Room>();
            string name = "";
            string des = "";
            IPEndPoint address = null;
            int roomFieldCount = 0;
            while (serializer.bytePos < serializer.byteLength)
            {
                SignalingAttribute attrType = (SignalingAttribute)serializer.ReadByte();
                attributeTypes.Add(attrType);

                switch (attrType)
                {
                    case SignalingAttribute.PeerAddress:
                        response.Add(attrType, ReadPeerAddress());
                        break;
                    case SignalingAttribute.RoomAddress:
                        //roomAddressList.Add(ReadPeerAddress());
                        roomFieldCount++;
                        address = ReadPeerAddress();
                        break;
                    case SignalingAttribute.RoomName:
                        //roomNameList.Add(serializer.ReadString());
                        name = ReadString();
                        roomFieldCount++;
                        break;
                    case SignalingAttribute.RoomDescription:
                        //roomDescriptionList.Add(serializer.ReadString());
                        des = ReadString();
                        roomFieldCount++;
                        break;
                    case SignalingAttribute.Failed:
                        response.Add(attrType, false);
                        break;
                    case SignalingAttribute.Success:
                        response.Add(attrType, true);
                        break;
                    default:
                        ushort attrLen = serializer.ReadUShort();
                        byte[] bytes = serializer.ReadBytes(attrLen);
                        response.Add(attrType, bytes);
                        while (((attrLen++) % 4) != 0)
                            serializer.ReadByte();
                        break;
                }
                if (roomFieldCount == 3)
                {
                    roomList.Add(new Room("", address, des, name));
                    roomFieldCount = 0;
                }
               
            }
            if (roomList.Count > 0)
                response.Add(SignalingAttribute.Room, roomList);
        }
        private string ReadString()
        {
            ushort attrLength = serializer.ReadUShort();
            if (attrLength == 0) return "";
            string ret = serializer.ReadString(attrLength);
            while ((serializer.bytePos < serializer.byteLength) && ((attrLength++) % 4) != 0)
                serializer.ReadByte();
            return ret;
        }
        private IPEndPoint ReadPeerAddress()
        {
            ushort attrLength = serializer.ReadUShort();
            IPEndPoint ipe = null;
            byte family = serializer.ReadByte();
            ushort port = serializer.ReadUShort();
            switch (family)
            {
                case 1:
                    ipe = new IPEndPoint(new IPAddress(serializer.ReadBytes(4)), port);
                    break;
                case 2:
                    ipe = new IPEndPoint(new IPAddress(serializer.ReadBytes(16)), port);
                    break;
            }
            while ((serializer.bytePos < serializer.byteLength) && ((attrLength++) % 4) != 0)
                serializer.ReadByte();
            return ipe;
        }
        private void LogAttribute(int index)
        {
            SignalingAttribute attr = attributeTypes[index];
            string valueStr = "";
            if (response.ContainsKey(attr))
            {
                object value = response[attr];

                if (value is string v)
                    valueStr = v;
                else if (value is byte[] b)
                    valueStr = NetworkSerializer.ByteArrayToHexString(b);
                else
                    valueStr = value.ToString();
            }
            
        }
        public object Get(SignalingAttribute attr)
        {
            if (!response.ContainsKey(attr))
                return null;
            object value = response[attr];
            return value;
        }
        #endregion
    }
}
