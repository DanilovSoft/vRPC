using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace DanilovSoft.vRPC
{
    public partial class VRpcListener
    {
        public static VRpcListener StartNew(IPAddress ipAddress)
        {
            var listener = new VRpcListener(ipAddress);
            listener.Start();
            return listener;
        }

        public static VRpcListener StartNew(IPAddress ipAddress, int port)
        {
            var listener = new VRpcListener(ipAddress, port);
            listener.Start();
            return listener;
        }
    }
}
