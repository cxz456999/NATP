using System;
using System.Collections.Generic;
using System.Text;

namespace NATP.Signaling.Server
{
    interface INATP_SignalingServerSender
    {
        long Send(byte[] buffer);
        bool Disconnect();

        void NATP_OnConnected();
    }
}
