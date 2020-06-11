using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NATP.STUN
{
    public interface INATP_STUN_Sender
    {
        long Send(byte[] data); 
    }
}
