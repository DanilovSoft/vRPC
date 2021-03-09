using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.AsyncEx;
using DanilovSoft.vRPC;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest
{
    public interface IClientTestController
    {
        string Echo(string msg);
        Task<string> EchoAsync(string msg);

        [Notification]
        void EchoNotification(int n);

        [JsonRpc]
        [Notification]
        void JEchoNotification(int n);
    }

    internal class ClientTestController : RpcController
    {
        private readonly IServiceProvider _serviceProvider;

        public ClientTestController(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public string Echo(string msg)
        {
            return msg;
        }

        public void EchoNotification(int n)
        {
            Debug.Assert(IsNotificationRequest);

            _serviceProvider.GetRequiredService<ManualResetEventSlim>().Set();
        }
        
        public void JEchoNotification(int n)
        {
            Debug.Assert(IsNotificationRequest);

            var mre = _serviceProvider.GetRequiredService<ManualResetEventSource<int>>();

            mre.TrySet(n);
        }
    }
}
