using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using vRPC;

namespace Client
{
    [ControllerContract("Home")]
    public interface IServerHomeController
    {
        void DummyCall();
        Task DummyCallAsync();
        Task Test3Async();
        Task<int> Test4Async();
        Task<int> Test2Async();
        Task<int> Test0Async();
    }
}
