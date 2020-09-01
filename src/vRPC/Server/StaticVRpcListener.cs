using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;

namespace DanilovSoft.vRPC
{
    public partial class VRpcListener
    {
        public static VRpcListener StartNew(IPAddress ipAddress)
        {
            var listener = new VRpcListener(ipAddress, 0, Assembly.GetCallingAssembly());
            listener.Start();
            return listener;
        }

        public static VRpcListener StartNew(IPAddress ipAddress, int port)
        {
            var listener = new VRpcListener(ipAddress, port, Assembly.GetCallingAssembly());
            listener.Start();
            return listener;
        }
    }
}
