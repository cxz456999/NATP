using NATP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace Signaling.Server
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
        /*public byte[]ToByteArray(Room obj)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }
        public static Room FromByteArray(byte[] arrBytes)
        {
            using (var memStream = new MemoryStream())
            {
                var binForm = new BinaryFormatter();
                memStream.Write(arrBytes, 0, arrBytes.Length);
                memStream.Seek(0, SeekOrigin.Begin);
                var obj = binForm.Deserialize(memStream);
                return (Room)obj;
            }
        }*/
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
     * 
     * attribute--
     * PeerAddress: one byte family, two bytes port, (4bytes IPv4/ 16bytes IPv6)
    */
    public class SignalingServerMessage
    {
        public bool IsMessage = false;
        public SignalingMethod methodType;

        public static bool CheckMessage(byte b) { return b == StartByte; }

        public static byte StartByte => 0x38;
        private Dictionary<SignalingAttribute, object> response = new Dictionary<SignalingAttribute, object>();
        private NetworkSerializer serializer = new NetworkSerializer(8092);
        private List<SignalingAttribute> attributeTypes = new List<SignalingAttribute>();
        private List<byte[]> attributeBytes = new List<byte[]>();
        
        public SignalingServerMessage()
        {

        }
        public SignalingServerMessage(SignalingMethod m)
        {
            methodType = m;
        }
        public bool FromBuffer(byte[] buffer, long offset, long size)
        {
            if (buffer[offset] != 0x38) { IsMessage = false;  return false; }
            serializer.SetBuffer(buffer, offset, size);
            serializer.ReadByte(); // read heard 00111000
            methodType = (SignalingMethod)serializer.ReadByte();
            if (!Enum.IsDefined(typeof(SignalingMethod), methodType)) { IsMessage = false; return false; }
            IsMessage = true;
            ReadAttribute();
            return true;
        }
        #region Read
        public void ReadAttribute()
        {
            Console.WriteLine("ReadAttribute");
            while (serializer.bytePos < serializer.byteLength)
            {
                SignalingAttribute attrType = (SignalingAttribute)serializer.ReadByte();
                attributeTypes.Add(attrType);
                
                switch (attrType)
                {
                    case SignalingAttribute.RoomAddress:
                    case SignalingAttribute.PeerAddress:
                        response.Add(attrType, ReadPeerAddress());
                        break;
                    case SignalingAttribute.RoomName:
                    case SignalingAttribute.RoomDescription:
                    case SignalingAttribute.RoomTag:
                        response.Add(attrType, ReadString());
                        break;
                    /*case SignalingAttribute.Room:
                        response.Add(attrType, ReadRoom());
                        break;*/
                    default:
                        ushort attrLen = serializer.ReadUShort();
                        byte[] bytes = serializer.ReadBytes(attrLen);
                        response.Add(attrType, bytes);
                        while (((attrLen++) % 4) != 0)
                            serializer.ReadByte();
                        break;
                }
            }
        }
        /*private Room ReadRoom()
        {
            
            ushort attrLength = serializer.ReadUShort();
            byte[] byteRoom = serializer.ReadBytes(attrLength);
            while ((serializer.bytePos < serializer.byteLength) && ((attrLength++) % 4) != 0)
                serializer.ReadByte();
            Room r = Room.FromByteArray(byteRoom);
            Console.WriteLine("ReadRoom: " + r.ToString());
            return r;
        }*/
        private string ReadString()
        {
            ushort attrLength = serializer.ReadUShort();
            if (attrLength == 0)
            {
                return "";
            }
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
            byte[] address;
            switch (family)
            {
                case 1:
                    address = serializer.ReadBytes(4);
                    Array.Reverse(address);
                    ipe = new IPEndPoint(new IPAddress(address), port);
                    break;
                case 2:
                    address = serializer.ReadBytes(16);
                    Array.Reverse(address);
                    ipe = new IPEndPoint(new IPAddress(address), port);
                    break;
            }
            while ((serializer.bytePos < serializer.byteLength) && ((attrLength++) % 4) != 0)
                serializer.ReadByte();
            return ipe;
        }
       
        public object Get(SignalingAttribute attr)
        {
            if (!response.ContainsKey(attr))
                return null;
            object value = response[attr];
            return value;
        }
        #endregion
        #region Write
        public byte[] WriteRequest()
        {
            
            serializer.SetBufferLength(0);

            serializer.Write((byte)0x38);

            //method id
            serializer.Write((byte)methodType);

            //attributes
            for (int i = 0; i < attributeBytes.Count; i++)
            {
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

            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteUInt(SignalingAttribute attr, uint value)
        {
            serializer.SetBufferLength(0);

            serializer.Write((byte)attr);
            serializer.Write((ushort)4);
            serializer.Write(value);

            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteEmpty(SignalingAttribute attr)
        {
            serializer.SetBufferLength(0);

            serializer.Write((byte)attr);
            serializer.Write((ushort)0);

            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }
        #endregion
        
    }
}
