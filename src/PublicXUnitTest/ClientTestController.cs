using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    }

    internal class ClientTestController : ClientController
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
            _serviceProvider.GetRequiredService<ManualResetEventSlim>().Set();
        }
    }
}
