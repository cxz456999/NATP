using StringPrep;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;


namespace NATP.STUN
{
    public enum STUNMethod
    {
        None = 0x0,

        //STUN
        BindingRequest = 0x0001,
        BindingResponse = 0x0101,
        BindingErrorResponse = 0x0111,
        SharedSecretRequest = 0x0002,
        SharedSecretResponse = 0x0102,
        SharedSecretErrorResponse = 0x0112,

        //TURN
        AllocateRequest = 0x0003,
        AllocateResponse = 0x0103,
        AllocateError = 0x0113,
        RefreshRequest = 0x0004,
        RefreshResponse = 0x0104,
        RefereshError = 0x0114,
        SendRequest = 0x0006,
        SendResponse = 0x0106,
        SendError = 0x0116,
        DataRequest = 0x0007,
        DataResponse = 0x0107,
        DataError = 0x0117,
        DataIndication = 0x0017,
        CreatePermissionRequest = 0x0008,
        CreatePermissionResponse = 0x0108,
        CreatePermissionError = 0x0118,
        ChannelBindRequest = 0x0009,
        ChannelBindResponse = 0x0109,
        ChannelBindError = 0x0119,
    }

    public enum STUNAttribute
    {
        None = 0x0,

        //STUN standard attributes
        MappedAddress = 0x0001,
        ResponseAddress = 0x0002,
        ChangeRequest = 0x0003,
        SourceAddress = 0x0004,
        ChangedAddress = 0x0005,
        Username = 0x0006,
        Password = 0x0007,
        MessageIntegrity = 0x0008,
        ErrorCode = 0x0009,
        UnknownAttribute = 0x000A,
        ReflectedFrom = 0x000B,
        XorMappedAddress = 0x0020,
        XorOnly = 0x0021,
        ServerName = 0x8022,
        OtherAddress = 0x802C,

        //TURN extras
        ChannelNumber = 0x000C,
        Lifetime = 0x000D,
        AlternateServer = 0x000E,
        Bandwidth = 0x0010,
        DestinationAddress = 0x0011,
        XorPeerAddress = 0x0012,
        Data = 0x0013,
        Realm = 0x0014,
        Nonce = 0x0015,
        XorRelayedAddress = 0x0016,
        EvenPort = 0x0018,
        RequestedTransport = 0x0019,
        DontFragment = 0x001A,
        TimerVal = 0x0021,
        ReservationToken = 0x0022
    }
    public class STUNAddress
    {
        public byte family = 0;
        public ushort port = 0;
        public byte[] address = null;
        public bool IsIPv4 => family == 0x1;
        public STUNAddress() { }
        public STUNAddress(IPEndPoint ipe)
        {
            port = (ushort)ipe.Port;
            address = ipe.Address.GetAddressBytes();
            if (address.Length > 4) family = 0x2;
            else family = 0x1;
        }
        public STUNAddress(string address, ushort port)
        {
            this.port = port;
            string[] parts = address.Split('.');
            if (parts.Length != 4)
            {
                family = 0x2;
                parts = address.Split(':');
                if (parts.Length != 8)
                    throw new Exception("IP address format Error");
            }
            else family = 0x1;
            if (family == 0x1)
            {
                this.address = new byte[4];
                for (int i = 0; i < parts.Length; i++)
                {
                    ushort us;
                    if (!ushort.TryParse(parts[i], out us))
                        throw new Exception("IP address format Error");
                    this.address[i] = (byte)((us) & 0xff);
                }
            }
            else
            {
                this.address = new byte[16];
                for (int i = 0; i < parts.Length; i++)
                {
                    ushort us;
                    if (!ushort.TryParse(parts[i], NumberStyles.HexNumber, null, out us))
                        throw new Exception("IP address format Error");
                    this.address[i * 2] = (byte)(us & 0xff);
                    this.address[i * 2 + 1] = (byte)((us >> 8) & 0xff);
                }
            }
        }
        public string ToIPString()
        {
            //ipv4
            if (family == 1)
                return address[0] + "." + address[1] + "." + address[2] + "." + address[3];
            //ipv6
            string ipv6 = "";
            for (int i = 0; i < address.Length; i += 2)
                ipv6 += BitConverter.ToUInt16(address, i) + ":";
            return ipv6.Substring(0, ipv6.Length - 1);
        }
        public override string ToString()
        {
            //return ToIPPortString(true);
            //ipv4
            if (family == 1)
                return address[0] + "." + address[1] + "." + address[2] + "." + address[3] + ":" + port;

            //ipv6
            string ipv6 = "";
            for (int i = 0; i < address.Length; i += 2)
                ipv6 += BitConverter.ToUInt16(address, i) + ":";
            return ipv6 + port;
        }
        public IPEndPoint ToIPEndPoint()
        {
            return new IPEndPoint(new IPAddress(address), port);
        }
    }
    public class MessageSTUN
    {
        #region STUN Stuff
        public const int PacketPoolBufferMaxLength = 3000;
        public bool IsSTUNMessage = true;
        public STUNMethod method = STUNMethod.None;
        public ushort methodLength = 0;
        public ushort methodId = 0;
        public byte[] transactionID = new byte[16];
        public uint magicCookie = 0x2112A442;

        public bool integrity = false;

        private NetworkSerializer serializer = new NetworkSerializer(PacketPoolBufferMaxLength);
        public List<STUNAttribute> attributeTypes = new List<STUNAttribute>();
        public List<byte[]> attributeBytes = new List<byte[]>();
        public Dictionary<STUNAttribute, object> response = new Dictionary<STUNAttribute, object>();
        #endregion

        #region request
        public string username = "";
        public string password = "";
        public string realm = "";
        #endregion

        #region response
        public uint responseChannelNumber = 0;
        public bool isIndicateData = false;
        public ushort IndicatePort => ((STUNAddress)Get(STUNAttribute.XorPeerAddress)).port;
        public STUNAddress indicateAddress => ((STUNAddress)Get(STUNAttribute.XorPeerAddress));
        #endregion
        public MessageSTUN(string usr, string pwd, string realm, STUNMethod method, byte[] transactionID) // for request
        {
            this.username = usr;
            this.password = pwd;
            this.realm = realm;
            this.method = method;
            this.transactionID = transactionID;
        }
        public MessageSTUN(byte[] receive, long offset, long size, out byte[] rcvData) // for response
        {
            ReadResponse(receive, offset, size, out rcvData);
        }
        public object Get(STUNAttribute attr)
        {
            if (!response.ContainsKey(attr))
                return null;
            object value = response[attr];
            return value;
        }

        public string GetString(STUNAttribute attr)
        {
            object obj = Get(attr);
            if (obj == null)
                return "";
            return obj.ToString();
        }

        public byte[] WriteRequest()
        {
            Console.WriteLine("\nRequest Method= " + Enum.GetName(typeof(STUNMethod), method));
            serializer.SetBufferLength(0);
            //method id
            serializer.Write((ushort)method);

            //method length
            serializer.Write((ushort)0);

            //transaction id 
            serializer.Write(transactionID);

            //attributes
            for (int i = 0; i < attributeBytes.Count; i++)
            {
                LogAttribute(i);
                serializer.Write(attributeBytes[i]);
            }

            //update message length
            int totalLength = serializer.byteLength - 20;
            int lastPos = serializer.byteLength;
            if (integrity)
                totalLength += 24;
            serializer.byteLength = 2;
            serializer.Write((ushort)totalLength);
            serializer.byteLength = lastPos;

            //method integrity goes here
            if (integrity)
            {
                //GenerateMessageIntegrity(packet);
                AddMessageIntegrity(serializer);
            }

            Console.WriteLine("Message Length = " + totalLength);
            Console.WriteLine("Total Bytes: \n\n" + serializer.byteLength);
            byte[] ret = serializer.ToArray();
            //cleanup
            attributeBytes.Clear();
            serializer.SetBufferLength(0);
            return ret;
        }

        public void WriteRequestIntegrity()
        {
            integrity = true;
        }

        public void AddMessageIntegrity(NetworkSerializer packet)
        {
            string saslPassword = new SASLprep().Prepare(password);

            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
            string valueToHashMD5 = string.Format("{0}:{1}:{2}", username, realm, saslPassword);
            byte[] hmacSha1Key = md5.ComputeHash(Encoding.UTF8.GetBytes(valueToHashMD5));

            HMACSHA1 hmacSha1 = new HMACSHA1(hmacSha1Key);
            byte[] hmacBytes = hmacSha1.ComputeHash(packet.ByteBuffer, 0, packet.byteLength);

            packet.Write((ushort)STUNAttribute.MessageIntegrity);
            packet.Write((ushort)hmacBytes.Length);
            packet.Write(hmacBytes);
        }

        public void LogAttribute(int index)
        {
            STUNAttribute attr = attributeTypes[index];
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
            Console.WriteLine("Write Attribute: " + Enum.GetName(typeof(STUNAttribute), attr) + " = " + valueStr);
        }

        public void WriteChangeRequest(bool changeIP, bool changePort)
        {
            serializer.SetBufferLength(0);

            serializer.Write((ushort)STUNAttribute.ChangeRequest);
            serializer.Write((ushort)4);

            int flags = (!changeIP ? 0 : (1 << 2)) | (!changePort ? 0 : (1 << 1));
            serializer.Write(flags);

            attributeTypes.Add(STUNAttribute.ChangeRequest);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteBytes(STUNAttribute attr, byte[] bytes)
        {
            serializer.SetBufferLength(0);
            serializer.Write((ushort)attr);
            serializer.Write((ushort)bytes.Length);
            serializer.Write(bytes);

            //pad to multiple of 4
            PadTo32Bits(bytes.Length, serializer);

            
            response.Add(attr, bytes);
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());

        }

        public void PadTo32Bits(int len, NetworkSerializer serializer)
        {
            while (((len++) % 4) != 0)
                serializer.Write((byte)0);
        }

        public void WriteString(STUNAttribute attr, string text)
        {
            serializer.SetBufferLength(0);

            int len = Encoding.UTF8.GetByteCount(text);

            serializer.Write((ushort)attr);
            serializer.Write(text, len);

            //pad to multiple of 4
            PadTo32Bits(len, serializer);

            response.Add(attr, text);
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteUInt(STUNAttribute attr, uint value)
        {
            serializer.SetBufferLength(0);

            serializer.Write((ushort)attr);
            serializer.Write((ushort)4);
            serializer.Write(value);

            response.Add(attr, value);
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void WriteEmpty(STUNAttribute attr)
        {
            serializer.SetBufferLength(0);

            serializer.Write((ushort)attr);
            serializer.Write((ushort)0);

            response.Add(attr, "");
            attributeTypes.Add(attr);
            attributeBytes.Add(serializer.ToArray());
        }

        public void ReadResponse(byte[] datas, long offset, long size, out byte[] rcvData)
        {
            serializer.SetBuffer(datas, offset, size);

            methodId = (ushort)((uint)serializer.ReadUShort() & 0x3FFF); //0x3F (0011 1111) sets left most 2 bits to 00
            methodLength = serializer.ReadUShort();
            transactionID = serializer.ReadBytes(16);

            if (!Enum.IsDefined(typeof(STUNMethod), (STUNMethod)methodId) || methodLength != serializer.byteLength - serializer.bytePos)
            {
                serializer.SetStartPos();
                isIndicateData = IsSTUNMessage = false;
                serializer.ReverserBuffer((int)offset, 4);
                ushort appSize = serializer.ReadUShort();
                responseChannelNumber = serializer.ReadUShort();
                rcvData = new byte[appSize];
                Array.Copy(datas, 4, rcvData, 0, appSize);
                return;
            }


            method = (STUNMethod)methodId;
            isIndicateData = method == STUNMethod.DataIndication;
            IsSTUNMessage = !isIndicateData;
            ReadAttribute(out rcvData);
            
        }

        public void ReadAttribute(out byte[] rcvData)
        {
            while (serializer.bytePos < serializer.byteLength && serializer.bytePos < methodLength)
            {
                STUNAddress address;
                STUNAttribute attrType = (STUNAttribute)serializer.ReadUShort();
                attributeTypes.Add(attrType);
                switch (attrType)
                {
                    case STUNAttribute.MappedAddress:
                    case STUNAttribute.SourceAddress:
                    case STUNAttribute.ChangedAddress:
                        address = ReadMappedAddress();
                        response.Add(attrType, address);
                        break;
                    case STUNAttribute.XorPeerAddress:
                    case STUNAttribute.XorMappedAddress:
                    case STUNAttribute.XorRelayedAddress:
                        address = ReadXorMappedAddress();
                        response.Add(attrType, address);
                        break;
                    case STUNAttribute.ErrorCode:
                        response.Add(attrType, ReadErrorCode());
                        break;
                    case STUNAttribute.UnknownAttribute:
                        response.Add(attrType, ReadUnknownAttributes());
                        break;
                    case STUNAttribute.ServerName:
                        response.Add(attrType, ReadString());
                        break;
                    case STUNAttribute.Realm:
                        response.Add(attrType, ReadString());
                        break;
                    case STUNAttribute.Username:
                        response.Add(attrType, ReadString());
                        break;
                    default:
                        ushort attrLen = serializer.ReadUShort();
                        byte[] bytes = serializer.ReadBytes(attrLen);
                        response.Add(attrType, bytes);
                        while (((attrLen++) % 4) != 0)
                            serializer.ReadByte();
                        break;
                }
            }
            rcvData = (byte[])Get(STUNAttribute.Data);
        }
        public string ReadString()
        {
            string value = serializer.ReadString();
            int len = Encoding.UTF8.GetByteCount(value);
            while (((len++) % 4) != 0)
                serializer.ReadByte();
            return value;
        }

        public string ReadErrorCode()
        {
            ushort attrLength = serializer.ReadUShort();
            uint bits = serializer.ReadUInt();
            uint code = bits & 0xFF;
            uint codeClass = (bits & 0x700) >> 8;
            string phrase = serializer.ReadString(attrLength - 4);
            while ((attrLength++) % 4 != 0)
                serializer.ReadByte();
            return "Error (" + codeClass + code.ToString("D2") + "): " + phrase;
        }

        public uint ReadUInt()
        {
            ushort attrLength = serializer.ReadUShort();
            uint value = serializer.ReadUInt();
            return value;
        }

        public string ReadUnknownAttributes()
        {
            ushort attrLength = serializer.ReadUShort();
            string attrs = "";// new string[attrLength / 2];
            attrLength += (ushort)(attrLength % 4);
            for (int i = 0; i < attrLength; i += 2)
            {
                if (i > 0)
                    attrs += ", ";
                ushort attrId = serializer.ReadUShort();
                try
                {
                    attrs += Enum.GetName(typeof(STUNAttribute), (STUNAttribute)attrId);
                }
                catch (Exception e)
                {
                    attrs += "" + attrId;
                }
            }
            if (attrLength % 4 != 0)
            {
                int temp = serializer.ReadUShort();
                Console.WriteLine("Extra Unknown Attr: " + temp);
            }
            return attrs;
        }

        public STUNAddress ReadMappedAddress()
        {
            STUNAddress sa = new STUNAddress();
            ushort attrLength = serializer.ReadUShort();
            byte empty = serializer.ReadByte();
            sa.family = serializer.ReadByte();
            sa.port = serializer.ReadUShort();

            switch (sa.family)
            {
                case 1:
                    sa.address = new byte[4];
                    break;
                case 2:
                    sa.address = new byte[16];
                    break;
            }

            for (int i = 0; i < sa.address.Length; i++)
                sa.address[i] = serializer.ReadByte();
            return sa;
        }

        public STUNAddress ReadXorMappedAddress()
        {
            STUNAddress sa = new STUNAddress();
            ushort attrLength = serializer.ReadUShort();
            ushort xorFlag16 = (ushort)(magicCookie >> 16);
            byte empty = serializer.ReadByte();
            sa.family = serializer.ReadByte();
            sa.port = (ushort)(serializer.ReadUShort() ^ xorFlag16);

            switch (sa.family)
            {
                case 1:
                    byte[] xorFlagBytes = new byte[4];
                    Array.Copy(serializer.ByteBuffer, 4, xorFlagBytes, 0, 4);
                    Array.Reverse(xorFlagBytes);
                    uint xorFlag32 = BitConverter.ToUInt32(xorFlagBytes, 0);

                    sa.address = new byte[4];
                    uint address = serializer.ReadUInt() ^ xorFlag32;
                    sa.address[0] = (byte)((address & 0xff000000) >> 24);
                    sa.address[1] = (byte)((address & 0x00ff0000) >> 16);
                    sa.address[2] = (byte)((address & 0x0000ff00) >> 8);
                    sa.address[3] = (byte)(address & 0x000000ff);
                    break;
                case 2:
                    sa.address = new byte[16];
                    byte[] xorFlags = new byte[16];
                    Array.Copy(transactionID, 0, xorFlags, xorFlags.Length, transactionID.Length);
                    for (int i = 0; i < sa.address.Length; i++)
                        sa.address[i] = (byte)(serializer.ReadByte() ^ xorFlags[i]);
                    break;
            }

            return sa;
        }
    }
}
