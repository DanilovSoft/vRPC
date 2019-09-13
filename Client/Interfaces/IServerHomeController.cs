using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    [ControllerContract("Home")]
    public interface IServerHomeController
    {
        DateTime DummyCall(string v);
        Task<DateTime> DummyCallAsync(string v, out int n);
        Task Test3Async();
        Task<int> Test4Async();
        Task<int> Test2Async();
        Task<int> Test0Async();
        [Notification]
        Task NotifyTestAsync();
    }
}
