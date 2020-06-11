using NATP.Signaling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Timers;

using Object = System.Object;

namespace NATP.STUN
{
    public class NATP_STUN_ServerDataReceivedEventArgs : EventArgs
    {
        public int connectionId;
        public ArraySegment<byte> segment;
        public NATP_STUN_ServerDataReceivedEventArgs(int _connectionId, ArraySegment<byte> _segment) => (connectionId, segment) = (_connectionId, _segment);
    }
    public class NATP_STUN_ClientDataReceivedEventArgs : EventArgs
    {
        public ArraySegment<byte> segment;
        public NATP_STUN_ClientDataReceivedEventArgs(ArraySegment<byte> segment) => this.segment = segment;
    }
    public class NATP_STUNCore
    {
        #region server events
        public event EventHandler<uint> OnServerConnected;
        public event EventHandler<NATP_STUN_ServerDataReceivedEventArgs> OnServerDataReceived;
        public event EventHandler<uint> OnServerDisconnected;
        #endregion

        public event EventHandler<NATP_SignalingEventArgs> OnAllocateResponseEvent;
        event Action OnRefreshLifeTimeResponseEvent;
        /*<summary>
               The LIFETIME attribute represents the duration for which the server
               will maintain an allocation in the absence of a refresh.  The TURN
               client can include the LIFETIME attribute with the desired lifetime
               in Allocate and Refresh requests.  The value portion of this
               attribute is 4 bytes long and consists of a 32-bit unsigned integral
               value representing the number of seconds remaining until expiration.
               channelbind liftime: 600s.
         </summary>*/
        public uint lifeTime = 550 * 1000; 
        /*<summary>
               if true, received a message from the peer which port is different from the peer you added in the begining, 
                        it will update the original port to the port you received in the Data indication.
               if false, trigger a event.
         </summary>*/
        public bool autoUpdatePort = true;

        /*<summary>
         * send/data indication not avalialbe now
         * </summary>*/
        public bool ChannelNumberMode = true;
        /*<summary>
               To count the times of allocation. In general, it needs to allcate twice.
         </summary>*/
        private int turnAllocateCount = 0;


        private string username = "";
        private string password = "";
        private string realm = "";

        private List<STUNAddress> peers;
        private Dictionary<uint, int> peersInTrashCan;
        private Timer timerForRefreshChannelBind;
        private Timer timerForCheckingConnection;
        private long serverHeartbeatCount = 0;
        private Dictionary<uint, long> peerHeartbeat;

        private byte[] transactionID = null;
        private uint magicCookie = 0x2112A442;
        private byte[] nonce = null;

        private Dictionary<string, uint> ip2ChannelNumber;
        private Dictionary<uint, STUNAddress> channelNumber2Peer;
        private uint channelNumberAvailalbe = 0x4001;
        private object _lock = new object();
        private object _lockHeartbeat = new object();
        private object _lockTrash = new object();
        public static readonly byte[] serverHeartbeat = new byte[4] { 255, 100, 50, 255 };
        public static readonly byte[] serverDisconnectHeartbeat = new byte[4] { 255, 202, 101, 255 };
        public static readonly byte[] clientHeartbeat = new byte[4] { 255, 50, 100, 200 };
        public static readonly byte[] clientDisconnectHeartbeat = new byte[4] { 255, 50, 40, 30 };
        private INATP_STUN_Sender sender;
        public NATP_STUNCore(INATP_STUN_Sender s)
        {
            this.sender = s;
            peers = new List<STUNAddress>();
            transactionID = GenerateTransactionID();
            ip2ChannelNumber = new Dictionary<string, uint>();
            channelNumber2Peer = new Dictionary<uint, STUNAddress>();
            peerHeartbeat = new Dictionary<uint, long>();
            peersInTrashCan = new Dictionary<uint, int>();
        }
        ~NATP_STUNCore()
        {
            Stop();
        }
        #region Public Method
        public void SetLoginInfo(string _user, string _pwd, string _realm)
        {
            this.username = _user;
            this.password = _pwd;
            this.realm = _realm;
            
        }
        public void Start()
        {
            if (username.Length == 0 || password.Length == 0 || realm.Length == 0)
                throw new Exception("Please set the login info before start.");
            
            Stop();
            RequestAllocate();
        }
        public void Stop()
        {
            if (timerForRefreshChannelBind != null) timerForRefreshChannelBind.Stop();
            if (timerForCheckingConnection != null) timerForCheckingConnection.Stop();
            
            peers.Clear();
            transactionID = GenerateTransactionID();
            ip2ChannelNumber.Clear();
            channelNumber2Peer.Clear();
            peerHeartbeat.Clear();
            peersInTrashCan.Clear();
            channelNumberAvailalbe = 0x4001;
            serverHeartbeatCount = 0;
            turnAllocateCount = 0;
        }
        public bool DisconnectClient(uint connectionId)
        {
            // keep ip2channelnumber and channelnumber2ip, 
            // because, before channel number binding delete from the stun server, 
            // client send a new channelbind request with different channel number of same peer will get error from the stun server
            // so keep the channel number info untile server delete the channelbind.
            // when channelbind expired on server, delete the record in ip2channelnumber and channelnumber2ip, so that client can genearte new channel number for this peer.

            // here use peersInTrashCan to keep the info of peer which disconnect from this server.
            // everytime, in RequestRefreshAllBindingChannelLifeTime function will check value of peersInTrashCan[connectionId],
            // if peersInTrashCan[connectionId] value is bigger than 2(start with 0), means client already refresh channelbind (550*3s). 
            // ensure there is enough time for stun server to  delete channelbind.

            if (RemovePeer(connectionId, false))
            {
                //lock (_lockTrash)
                {
                    peersInTrashCan[connectionId] = 0;
                }
                OnServerDisconnected?.Invoke(this, connectionId);
                return true;
            }
            return false;
        }
        public string GetClientAddress(uint connectionId)
        {
            lock (_lock)
            {
                if (channelNumber2Peer.ContainsKey(connectionId))
                    return channelNumber2Peer[connectionId].ToString();
            }
            return "";
        }
        #endregion
        #region Translate Message
        private byte[] AddIndicationHeader(STUNAddress stunAddress, byte[]data)
        {
            // send indication
            MessageSTUN message = CreateMessageSTUN(STUNMethod.DataIndication);
            byte[] ret;
            if (stunAddress.IsIPv4)
            {
                ret = new byte[8];
                ret[1] = (byte)(0x01); // family
            }
            else
            {
                ret = new byte[16];
                ret[1] = (byte)(0x02); // family
            }
            ret[0] = (byte)(0x0);

            ret[3] = (byte)(stunAddress.port & 0xff);
            ret[2] = (byte)((stunAddress.port >> 8) & 0xff);
            ret[2] ^= transactionID[0];
            ret[3] ^= transactionID[1];

            for (int i = 0; i < stunAddress.address.Length; i++)
            {
                ret[4 + i] = (byte)(stunAddress.address[i] ^ transactionID[i]);
            }

            message.WriteBytes(STUNAttribute.XorPeerAddress, ret);
            message.WriteEmpty(STUNAttribute.DontFragment);
            message.WriteBytes(STUNAttribute.Data, data);
            return message.WriteRequest();
        }
        private byte[] AddChannelNumberHeader(ushort connectionId, byte[] data)
        {
            // channel number
            byte[] sndDatas = new byte[data.Length + 4];
            ushort usCN = (ushort)connectionId;
            sndDatas[1] = (byte)(usCN & 0xff);
            sndDatas[0] = (byte)((usCN >> 8) & 0xff);
            ushort usLen = (ushort)data.Length;
            sndDatas[3] = (byte)(usLen & 0xff);
            sndDatas[2] = (byte)((usLen >> 8) & 0xff);
            Array.Copy(data, 0, sndDatas, 4, data.Length);
            return sndDatas;
        }
        public byte[] Translate(STUNAddress ipaddress, byte[] data)
        {
            return Translate(ipaddress.ToString(), data);
        }
        public byte[] Translate(uint connectionId, byte[] data)
        {
            if (!channelNumber2Peer.ContainsKey((uint)connectionId))
            {
                return null;
            }
            return ChannelNumberMode ? AddChannelNumberHeader((ushort)connectionId, data) : AddIndicationHeader(channelNumber2Peer[(ushort)connectionId], data);
        }
        public byte[] Translate(string ipaddress, byte[] data)
        {
            lock (_lock)
            {
                if (!ip2ChannelNumber.ContainsKey(ipaddress))
                {
                    return null;
                }
                ushort usCN = (ushort)ip2ChannelNumber[ipaddress];
                return ChannelNumberMode ? AddChannelNumberHeader(usCN, data): AddIndicationHeader(channelNumber2Peer[usCN], data);
            }
        }
        public byte[] Translate(STUNAddress peer, string text)
        {
            byte[] utfBytes = Encoding.UTF8.GetBytes(text);
            string ipaddress = peer.ToString();
            return Translate(ipaddress, utfBytes);
        }
        #endregion
        #region Peer
        public void AddPeerAndBind(IPEndPoint ipe)
        {
            if (AddPeer(ipe))
                RequestRefreshAllBindingChannelLifeTime();   
        }
        private bool AddPeer(IPEndPoint ipe)
        {
            return AddPeer(new STUNAddress(ipe));
        }
        private bool AddPeer(string peerAddr, ushort peerPort)
        {
            return AddPeer(new STUNAddress(peerAddr, peerPort));
        }
        private bool AddPeer(STUNAddress addr)
        {
            lock (_lock)
            {
                if (ip2ChannelNumber.ContainsKey(addr.ToString())) return false; 
            }
            peers.Add(addr);
            var id = GenerateChannelNumberForPeer(addr);
            lock(_lockHeartbeat)
            {
                peerHeartbeat[id] = -2;// (int)((double)lifeTime / (double)STUNClient.HeartbeatAfterConnected + 0.5);
            }

            if (peersInTrashCan.ContainsKey(id)) peersInTrashCan.Remove(id);
            return true;
           
        }
        public bool RemovePeer(string key, bool cleanChannelNumber = true)
        {
            lock (_lock)
            {
                if (!ip2ChannelNumber.ContainsKey(key)) return false;
                if (cleanChannelNumber)
                {
                    channelNumber2Peer.Remove(ip2ChannelNumber[key]);
                    ip2ChannelNumber.Remove(key);
                }
                peers.RemoveAll(p => p.ToString() == key);
                return true;
            }
        }
        public bool RemovePeer(uint id, bool cleanChannelNumber=true)
        {
            lock (_lock)
            {
                if (!channelNumber2Peer.ContainsKey(id)) return false;
                string key = channelNumber2Peer[id].ToString();
                if (cleanChannelNumber)
                {
                    ip2ChannelNumber.Remove(key);
                    channelNumber2Peer.Remove(id);
                }
                peers.RemoveAll(p => p.ToString() == key);
                return true;
            }
        }
        #endregion
        #region Heartbeat
        private bool IsClientHeartbeat(byte[] data)
        {
            if (data.Length != 4 ||
                data[3] != clientHeartbeat[3] || data[2] != clientHeartbeat[2] ||
                data[1] != clientHeartbeat[1] || data[0] != clientHeartbeat[0])
                return false;
            return true;
        }
        private bool IsClientDisconnectHeartbeat(byte[] data)
        {
            if (data.Length != 4 ||
                data[3] != clientDisconnectHeartbeat[3] || data[2] != clientDisconnectHeartbeat[2] ||
                data[1] != clientDisconnectHeartbeat[1] || data[0] != clientDisconnectHeartbeat[0])
                return false;
            return true;
        }
        private void SendDisconnectHeartbeatToAllClient()
        {
            for (int i = 0; i < peers.Count; i++)
            {
                
                sender.Send(Translate(ip2ChannelNumber[peers[i].ToString()], serverDisconnectHeartbeat));
            }
                
        }
        private void SendHeartBeatToClient(uint connectionId)
        {
            lock (_lockHeartbeat)
            {
                if (!peerHeartbeat.ContainsKey((uint)connectionId) || peerHeartbeat[(uint)connectionId] < 0) 
                    OnServerConnected?.Invoke(this, (uint)connectionId);// OnServerConnected?.BeginInvoke(this, (uint)connectionId, EndAsyncEvent, null);
                peerHeartbeat[(uint)connectionId] = serverHeartbeatCount;
            }

            sender.Send(Translate(connectionId, serverHeartbeat));
        }
        #endregion
        #region On STUN Response
        public virtual void OnResponse(byte[] buffer, long offset, long size)
        {
            byte[] appData;
            MessageSTUN message = new MessageSTUN(buffer, offset, size, out appData);
            if (!message.IsSTUNMessage) // Normal Data Received
            {
                string ip;
                uint responseChannelNumber;
                if (message.isIndicateData) // sender's ip and channel bind's ip are same, but port are different, so stun server send indicate data
                {
                    if (!autoUpdatePort)
                    {
                        // deal with it
                        return;
                    }

                    if (ChannelNumberMode)
                    {
                        AddPeer(message.indicateAddress);
                        RequestChannelBind(message.indicateAddress);
                        return;
                    }
                    else
                    {
                        ip = message.indicateAddress.ToString();
                        if (!ip2ChannelNumber.ContainsKey(ip))
                        {
                            AddPeer(message.indicateAddress);
                            return;
                        }
                    }
                    responseChannelNumber = ip2ChannelNumber[ip];
                }
                else
                {
                    responseChannelNumber = message.responseChannelNumber;
                }
                
                if (IsClientHeartbeat(appData))
                {
                    
                    SendHeartBeatToClient(responseChannelNumber);
                }
                else if (IsClientDisconnectHeartbeat(appData))
                {
                    lock(_lockHeartbeat)
                    {
                        peerHeartbeat.Remove(responseChannelNumber);
                    }
                    DisconnectClient(responseChannelNumber);
                }
                else
                {
                    OnServerDataReceived?.Invoke(this, new NATP_STUN_ServerDataReceivedEventArgs((int)responseChannelNumber, new ArraySegment<byte>(appData)));
                    //OnServerDataReceived?.BeginInvoke(this, new STUN_ServerDataReceivedEventArgs((int)responseChannelNumber, new ArraySegment<byte>(appData)), EndAsyncEvent, null);
                }
            }
            else // Is STUN Server response
            {
                var newNonce = (byte[])message.Get(STUNAttribute.Nonce);
                if (newNonce != null) nonce = newNonce;
                switch (message.method)
                {
                    case STUNMethod.AllocateResponse:
                        OnAllocateResponse(message);
                        break;
                    case STUNMethod.BindingResponse:
                        OnBindingResponse(message);
                        break;
                    case STUNMethod.ChannelBindError:
                        OnChannelBindError(message);
                        break;
                    case STUNMethod.ChannelBindResponse:
                        OnChannelBindResponse(message);
                        break;
                    case STUNMethod.AllocateError:
                        OnAllocateError(message);
                        break;
                    case STUNMethod.RefreshResponse:
                        OnRefreshResponse(message);
                        break;
                    case STUNMethod.RefereshError:
                        OnRefreshErrorResponse(message);
                        break;
                    case STUNMethod.CreatePermissionResponse:
                        OnCreatePermissionResponse(message);
                        break;
                }
            }
        }

        public virtual void OnBindingResponse(MessageSTUN message)
        {
        }
        public virtual void OnChannelBindResponse(MessageSTUN message)
        {
        }
        public virtual void OnAllocateResponse(MessageSTUN message)
        {
            lock (_lock)
            {
                ip2ChannelNumber.Clear();
                channelNumber2Peer.Clear();
            }
            GenerateChannelNumberForAllPeers();
            RequestRefreshAllBindingChannelLifeTime();
            StartRefreshTimer();
            StartCheckingConnectionTimer();
            
            STUNAddress sa = (STUNAddress)message.Get(STUNAttribute.XorRelayedAddress);
            //OnServerDataReceived?.BeginInvoke(this, new STUN_ServerDataReceivedEventArgs((int)responseChannelNumber, new ArraySegment<byte>(appData)), EndAsyncEvent, null);
            OnAllocateResponseEvent?.Invoke(this, new NATP_SignalingEventArgs(true, "", new IPEndPoint(new IPAddress(sa.address), sa.port)));
        }
        public virtual void OnRefreshResponse(MessageSTUN message)
        {
            OnRefreshLifeTimeResponseEvent?.Invoke();
        }
        public virtual void OnRefreshErrorResponse(MessageSTUN message)
        {
            RequestRefreshAllocationeLifeTime(this.lifeTime);
            RequestRefreshAllBindingChannelLifeTime();
            StartRefreshTimer();
        }
        public virtual void OnCreatePermissionResponse(MessageSTUN message)
        {
        }
        public virtual void OnAllocateError(MessageSTUN message)
        {
            RequestAllocate();
        }
        public virtual void OnChannelBindError(MessageSTUN message)
        {
            Start();
        }
        #endregion
        #region STUN Request
        public void RequestRefreshAllocationeLifeTime(uint lifetime)
        {
            MessageSTUN message = CreateMessageSTUN(STUNMethod.RefreshRequest);
            message.WriteString(STUNAttribute.Username, message.username);
            message.WriteString(STUNAttribute.Realm, message.realm);
            message.WriteUInt(STUNAttribute.Lifetime, lifetime);
            //message.WriteUInt(STUNAttribute.RequestedTransport, (uint)(17 << 24));

            if (nonce != null)
            {
                message.WriteBytes(STUNAttribute.Nonce, nonce);
                message.WriteRequestIntegrity();
            }

            byte[] pkg = message.WriteRequest();
            sender.Send(pkg);
        }
        public void RequestRefreshAllBindingChannelLifeTime()
        {
            for (int i = 0; i < peers.Count; i++)
            {
                if (ChannelNumberMode) RequestChannelBind(peers[i]);
                else RequestCreatePermission(peers[i]);
            }

            var ids = new List<uint>(peersInTrashCan.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                if (peersInTrashCan[ids[i]]++ >= 2)
                {
                    RemovePeer(ids[i]);
                }
            }
        }
        public void RequestAllocate()
        {
            if (++turnAllocateCount > 2) return;
            
            MessageSTUN message = CreateMessageSTUN(STUNMethod.AllocateRequest);
            //message.WriteString(STUNAttribute.ServerName, "Coturn-4.5.1.1 'dan Eider'");

            message.WriteString(STUNAttribute.Username, message.username);
            message.WriteString(STUNAttribute.Realm, message.realm);
            //message.WriteString(STUNAttribute.Password, message.password);

            message.WriteUInt(STUNAttribute.Lifetime, lifeTime);
            message.WriteEmpty(STUNAttribute.DontFragment);
            message.WriteUInt(STUNAttribute.RequestedTransport, (uint)(17 << 24));//(uint)(17 << 24)

            if (nonce != null)
            {
                message.WriteBytes(STUNAttribute.Nonce, nonce);
                message.WriteRequestIntegrity();
            }

            byte[] pkg = message.WriteRequest();
            sender.Send(pkg);
        }
        public void RequestChannelBind(STUNAddress stunAddress)
        {
            lock (_lock)
            {
                MessageSTUN message = CreateMessageSTUN(STUNMethod.ChannelBindRequest);
                // channel number [0x4000 through 0x4FFF](uint)random.Next(0x4001, 0x4FFF);
                uint channelNumber;
                if (!ip2ChannelNumber.ContainsKey(stunAddress.ToString()))
                    GenerateChannelNumberForPeer(stunAddress);
                channelNumber = ip2ChannelNumber[stunAddress.ToString()];
                Console.WriteLine("RequestChannelBind: {0} {1}:{2}", channelNumber, stunAddress.ToString(), stunAddress.port);
                channelNumber = (uint)(channelNumber << 16);
                message.WriteString(STUNAttribute.Username, message.username);
                message.WriteString(STUNAttribute.Realm, message.realm);

                message.WriteUInt(STUNAttribute.ChannelNumber, channelNumber);

                byte[] ret;
                if (stunAddress.IsIPv4)
                {
                    ret = new byte[8];
                    ret[1] = (byte)(0x01); // family
                }
                else
                {
                    ret = new byte[16];
                    ret[1] = (byte)(0x02); // family
                }
                ret[0] = (byte)(0x0);
                
                ret[3] = (byte)(stunAddress.port & 0xff);
                ret[2] = (byte)((stunAddress.port >> 8) & 0xff);
                ret[2] ^= transactionID[0];
                ret[3] ^= transactionID[1];

                for (int i = 0; i < stunAddress.address.Length; i++)
                {
                    ret[4 + i] = (byte)(stunAddress.address[i] ^ transactionID[i]);
                }

                message.WriteBytes(STUNAttribute.XorPeerAddress, ret);
                if (nonce != null)
                {
                    message.WriteBytes(STUNAttribute.Nonce, nonce);
                    message.WriteRequestIntegrity();
                }
                byte[] pkg = message.WriteRequest();
                sender.Send(pkg);
            }
        }
        public void RequestCreatePermission(STUNAddress stunAddress)
        {
            lock (_lock)
            {

                MessageSTUN message = CreateMessageSTUN(STUNMethod.CreatePermissionRequest);
                message.WriteString(STUNAttribute.Username, message.username);
                message.WriteString(STUNAttribute.Realm, message.realm);

                byte[] ret;
                if (stunAddress.IsIPv4)
                {
                    ret = new byte[8];
                    ret[1] = (byte)(0x01); // family
                }
                else
                {
                    ret = new byte[16];
                    ret[1] = (byte)(0x02); // family
                }
                ret[0] = (byte)(0x0);
                ushort xorFlag16 = (ushort)(magicCookie >> 16);

                ret[3] = 0;
                ret[2] = 0;
                ret[2] ^= transactionID[0];
                ret[3] ^= transactionID[1];

                for (int i = 0; i < stunAddress.address.Length; i++)
                {
                    ret[4 + i] = (byte)(stunAddress.address[i] ^ transactionID[i]);
                }

                message.WriteBytes(STUNAttribute.XorPeerAddress, ret);
                if (nonce != null)
                {
                    message.WriteBytes(STUNAttribute.Nonce, nonce);
                    message.WriteRequestIntegrity();
                }
                byte[] pkg = message.WriteRequest();
                sender.Send(pkg);
            }
        }
        #endregion
        #region Timer
        private void StartRefreshTimer()
        {
            if (timerForRefreshChannelBind != null) timerForRefreshChannelBind.Stop();
            // Create a timer with a two second interval.
            timerForRefreshChannelBind = new System.Timers.Timer(lifeTime);
            // Hook up the Elapsed event for the timer. 
            timerForRefreshChannelBind.Elapsed += OnRefreshEvent;
            timerForRefreshChannelBind.AutoReset = true;
            timerForRefreshChannelBind.Enabled = true;
        }

        private void OnRefreshEvent(Object source, ElapsedEventArgs e)
        {
            RequestRefreshAllocationeLifeTime(lifeTime);
            RequestRefreshAllBindingChannelLifeTime();
        }
        private void StartCheckingConnectionTimer()
        {
            if (timerForCheckingConnection != null) timerForCheckingConnection.Stop();
            // Create a timer with a two second interval.
            timerForCheckingConnection = new System.Timers.Timer(NATP_STUNClient.HeartbeatAfterConnected);
            // Hook up the Elapsed event for the timer. 
            timerForCheckingConnection.Elapsed += OnCheckingConnectionEvent;
            timerForCheckingConnection.AutoReset = true;
            timerForCheckingConnection.Enabled = true;
        }
        private void OnCheckingConnectionEvent(Object source, ElapsedEventArgs e)
        {
            if (peerHeartbeat.Count == 0) return;
            lock (_lockHeartbeat)
            {
                var peerIds = new List<uint>(peerHeartbeat.Keys);
                foreach (var id in peerIds)
                {
                    if (peerHeartbeat[id] < -1) 
                        peerHeartbeat[id]++;
                    else if (peerHeartbeat[id] < -200 || peerHeartbeat[id] == -1 || (serverHeartbeatCount - peerHeartbeat[id] > 1 && serverHeartbeatCount > 1))
                    {
                        peerHeartbeat.Remove(id);
                        DisconnectClient(id);
                    }
                    
                }
            }
            if (serverHeartbeatCount == long.MaxValue) serverHeartbeatCount = 0;
            else serverHeartbeatCount++;
        }
        #endregion
        #region Utilities
        private void AddChannelNumberAvailalbe()
        {
            if (channelNumberAvailalbe < 0x4FFF) channelNumberAvailalbe += 0x1;
            else channelNumberAvailalbe = 0x4001;
        }
        private uint GenerateChannelNumberForPeer(STUNAddress peer)
        {
            string key = peer.ToString();
            lock (_lock)
            {
                if (ip2ChannelNumber.ContainsKey(key)) return ip2ChannelNumber[key];
                // channel number [0x4000 through 0x4FFF]
                while (channelNumber2Peer.ContainsKey(channelNumberAvailalbe))
                    AddChannelNumberAvailalbe();
                channelNumber2Peer[channelNumberAvailalbe] = peer;
                uint channelnumber = channelNumberAvailalbe;
                ip2ChannelNumber[key] = channelNumberAvailalbe;
                AddChannelNumberAvailalbe();
                return channelnumber;
            }
        }
        private void GenerateChannelNumberForAllPeers()
        {
            for (int i = 0; i < peers.Count; i++)
            {
                GenerateChannelNumberForPeer(peers[i]);
            }
        }
        public MessageSTUN CreateMessageSTUN(STUNMethod method)
        {
            return new MessageSTUN(username, password, realm, method, transactionID);
        }
        public string GetAttributeKeys(MessageSTUN message)
        {
            string attrKeys = "";
            foreach (KeyValuePair<STUNAttribute, object> entry in message.response)
            {
                string key = Enum.GetName(typeof(STUNAttribute), entry.Key);
                int id = (int)entry.Key;
                if (attrKeys.Length > 0)
                    attrKeys += "\n";
                object value = entry.Value;
                string valueStr = "";
                if (value is string v)
                    valueStr = v;
                else if (value is byte[] b)
                    valueStr = NetworkSerializer.ByteArrayToHexString(b);
                else
                    valueStr = value.ToString();
                attrKeys += key + "(" + id.ToString("X") + ") = " + valueStr;
            }
            return attrKeys;
        }
        public static byte[] GenerateTransactionID()
        {
            Guid guid = Guid.NewGuid();
            byte[] bytes = guid.ToByteArray();
            byte[] magicCookie = BitConverter.GetBytes(0x2112A442);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(magicCookie);
            Array.Copy(magicCookie, 0, bytes, 0, magicCookie.Length);
            return bytes;
        }
        public static uint StringIPToHexIP(string strIP)
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
        public static byte[] ObjectToByteArray(object obj)
        {
            if (obj == null)
                return null;
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }
        #endregion
        #region Others
        //Callback for BeginInvoke
        /*private void EndAsyncEvent(IAsyncResult iar)
        {
            var ar = (AsyncResult)iar;
            var invokedMethod = (EventHandler)ar.AsyncDelegate;
            try
            {
                invokedMethod.EndInvoke(iar);
            }
            catch
            {
                // Handle any exceptions that were thrown by the invoked method
                Console.WriteLine("An event listener went kaboom!");
            }
        }*/
        #endregion
    }
}
