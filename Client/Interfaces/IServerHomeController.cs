using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    [ControllerContract("Home")]
    internal interface IServerHomeController
    {
        void DummyCall(int n);
        Task DummyCallAsync(int n);
        Task Test3Async();
        Task<int> Test4Async();
        Task<int> Test2Async();
        Task<int> Test0Async();

        //[Notification]
        void NotifyTest();

        DateTime Test(TestDto testDto);
        Task<string> TestAsync();
    }
}
