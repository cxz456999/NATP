using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using Timer = System.Timers.Timer;
using UdpClient = NetCoreServer.UdpClient;
using Object = System.Object;

namespace NATP.STUN
{

    public class NATP_STUNClient : UdpClient, INATP_STUN_Sender
    {
        #region client events
        public event EventHandler OnClientConnected;
        public event EventHandler<NATP_STUN_ClientDataReceivedEventArgs> OnClientDataReceived;
        public event EventHandler OnClientDisconnected;
        #endregion
        public NATP_STUNCore Core => stunCore;

        private bool _stop;
        private NATP_STUNCore stunCore;
        private bool usingSTUN;

        private Timer timerForSendingHeartbeat;
        public static readonly int HeartbeatBeforeConnected = 2 * 1000; // million sec
        public static int HeartbeatAfterConnected = 5 * 1000; // million sec
        public static int ReceiveBufferSize;
        public static int SendBufferSize;

        private bool IsConnectedToSTUNServer = false;
        private bool IsConnectedToHost = false;

        private long sendHeartbeatCount = 0;
        private long receivedHeartBeatCount = 0;
        private object _lockHeartbeat = new object();
        public new bool IsConnected => (IsConnectedToHost && !usingSTUN) || (usingSTUN && IsConnectedToSTUNServer);

        public NATP_STUNClient(string address, int port, bool _usingSTUN) : base(address, port)
        {
            usingSTUN = _usingSTUN;
            if (usingSTUN)
                stunCore = new NATP_STUNCore(this);
        }
        public NATP_STUNClient() { }
        ~NATP_STUNClient()
        {
            DisconnectAndStop();
        }
        public void SetLoginInfo(string _user, string _pwd, string _realm)
        {
            if (usingSTUN)  Core.SetLoginInfo(_user, _pwd, _realm);
            
        }
        public void DisconnectAndStop()
        {
            if (timerForSendingHeartbeat != null) timerForSendingHeartbeat.Stop();
            if (stunCore != null) stunCore.Stop();
            else Send(NATP_STUNCore.clientDisconnectHeartbeat);
            _stop = true;
            sendHeartbeatCount = 0;
            receivedHeartBeatCount = 0;
            Disconnect();
            //while (IsConnected)
            //    Thread.Yield();
        }
        #region Network Events
        protected override void OnConnected()
        {
            Console.WriteLine($"Echo UDP client connected a new session with Id {Id}");

            NATP_OnConnected();
            // Start receive datagrams
            ReceiveAsync();
        }

        protected override void OnDisconnected()
        {
            NATP_OnDisconnected();
            if (!_stop)
                Connect();
            _stop = false;
        }
        protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {

            NATP_OnReceived(endpoint, buffer, offset, size);
            // Continue receive datagrams
            ReceiveAsync();

        }

        protected override void OnError(SocketError error)
        {
            //Console.WriteLine($"Echo UDP client caught an error with code {error}");
        }
        #endregion
        #region Translate & Send
        public bool TranslateSend(int connectionId, byte[] datas, int offset, int size)
        {
            if (!usingSTUN || stunCore == null)
            {
                //throw new Exception("Turn service is Off");
                return false;
            }
            byte[] d = new byte[size];
            Array.Copy(datas, offset, d, 0, size);
            d = stunCore.Translate((uint)connectionId, d);
            return base.Send(d)>0;
        }
        public bool TranslateSend(STUNAddress ip, byte[] datas)
        {
            if (!usingSTUN || stunCore == null)
            {
                //throw new Exception("Turn service is Off");
                return false;
            }
            datas = stunCore.Translate(ip, datas);
            return base.Send(datas)>0;
        }
        public bool TranslateSend(STUNAddress ip, string text)
        {
            if (!usingSTUN || stunCore == null)
            {
                //throw new Exception("Turn service is Off");
                return false;
            }
            return base.Send(stunCore.Translate(ip, text))>0;
        }
        #endregion
        #region NATP Network Event
        private void NATP_OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {

            if (usingSTUN) // serve as server
            {
                stunCore.OnResponse(buffer, offset, size);
            }
            else // serve as client
            {
                // check if data is heartbeat
                if (IsServerHeartbeat(buffer, offset, size))
                {
                    lock (_lockHeartbeat)
                    {
                        if (receivedHeartBeatCount == long.MaxValue) receivedHeartBeatCount = 0;
                        else receivedHeartBeatCount++;
                    }
                    // if it's fisrt time receive the heartbeat from server, means connect to server.
                    if (!IsConnectedToHost)
                    {
                        IsConnectedToHost = true;
                        StartHeartbeatTimer(HeartbeatAfterConnected);
                        //OnClientConnected?.BeginInvoke(this, EventArgs.Empty, EndAsyncEvent, null);
                        OnClientConnected?.Invoke(this, EventArgs.Empty);
                    }
                }
                /* else if (IsServerDisconnectHeartbeat(buffer, offset, size))
                 {
                     DisconnectAndStop();
                 }*/
                else // normal data
                {
                    byte[] pureData = new byte[size];
                    Array.Copy(buffer, offset, pureData, 0, size);
                    OnClientDataReceived?.Invoke(this, new NATP_STUN_ClientDataReceivedEventArgs(new ArraySegment<byte>(pureData, 0, (int)size)));
                    //OnClientDataReceived?.BeginInvoke(this, new STUN_ClientDataReceivedEventArgs(new ArraySegment<byte>(pureData, 0, (int)size)), EndAsyncEvent, null);
                }

            }
        }
        private void NATP_OnConnected()
        {
            if (usingSTUN && stunCore != null) // serve as server
            {
                IsConnectedToSTUNServer = true;
                stunCore.Start();
            }
            else // serve as client
            {
                StartHeartbeatTimer(HeartbeatBeforeConnected);
            }
        }
        private void NATP_OnDisconnected()
        {
            IsConnectedToHost = false;
            IsConnectedToSTUNServer = false;

            OnClientDisconnected?.Invoke(this, EventArgs.Empty);
        }
        #endregion
        #region Heartbeat
        private bool IsServerHeartbeat(byte[] buffer, long offset, long size)
        {
            if (size != 4 ||
                buffer[offset + 3] != NATP_STUNCore.serverHeartbeat[3] || buffer[offset + 2] != NATP_STUNCore.serverHeartbeat[2] ||
                buffer[offset + 1] != NATP_STUNCore.serverHeartbeat[1] || buffer[offset] != NATP_STUNCore.serverHeartbeat[0])
                return false;
            return true;
        }
        private bool IsServerDisconnectHeartbeat(byte[] buffer, long offset, long size)
        {
            if (size != 4 ||
                buffer[offset + 3] != NATP_STUNCore.serverDisconnectHeartbeat[3] || buffer[offset + 2] != NATP_STUNCore.serverDisconnectHeartbeat[2] ||
                buffer[offset + 1] != NATP_STUNCore.serverDisconnectHeartbeat[1] || buffer[offset] != NATP_STUNCore.serverDisconnectHeartbeat[0])
                return false;
            return true;
        }
        private void StartHeartbeatTimer(long time)
        {
            if (timerForSendingHeartbeat != null) timerForSendingHeartbeat.Stop();
            // Create a timer with a two second interval.
            timerForSendingHeartbeat = new System.Timers.Timer(time);
            // Hook up the Elapsed event for the timer. 
            timerForSendingHeartbeat.Elapsed += OnHeartbeatEvent;
            timerForSendingHeartbeat.AutoReset = true;
            timerForSendingHeartbeat.Enabled = true;
        }
        private void OnHeartbeatEvent(Object source, ElapsedEventArgs e)
        {
            SendAsync(NATP_STUNCore.clientHeartbeat);
            long rc=0;
            lock(_lockHeartbeat)
            {
                rc = receivedHeartBeatCount;
            }
            if (sendHeartbeatCount - rc > 1)
            {
                DisconnectAndStop();
                return;
            }
            if (sendHeartbeatCount == long.MaxValue) sendHeartbeatCount = 0;
            else sendHeartbeatCount++;
            
        }
        #endregion
        #region Server
        public bool DisconnectClient(uint connectionId)
        {
            return Core.DisconnectClient(connectionId);
        }
        public string GetClientAddress(uint connectionId)
        {
            return Core.GetClientAddress(connectionId);
        }
        #endregion
        #region Others

        #endregion
    }
}
