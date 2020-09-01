using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC;

namespace XUnitTest
{
    public interface IServerTestController
    {
        void TestException(string exceptionMessage);
        void TestExceptionThrow(string exceptionMessage);
        void TestDelay();
        Task Test2Async();
        int GetSum(int x1, int x2);
        int GetSum2(int x1, int x2);
        Task<int> GetSumAsync(int x1, int x2);
        Task<string> GetNullStringAsync();
        string GetString();
        string GetNullString();

        [Notification]
        void Notify(int n);
        [Notification]
        Task NotifyAsync(int n);
        [Notification]
        void NotifyCallback(int n);

        string MakeCallback(string msg);
        string MakeAsyncCallback(string msg);
        void NotExistedMethod();
        [JsonRpc]
        void JNotExistedMethod();
    }

    [AllowAnonymous]
    internal class ServerTestController : ServerController
    {
        public void TestExceptionThrow(string message)
        {
            throw new VRpcBadRequestException(message);
        }

        public IActionResult TestException(string message)
        {
            return BadRequest(message);
        }

        public void TestDelay()
        {
            Thread.Sleep(500);
        }

        public int GetSum(int x1, int x2)
        {
            return x1 + x2;
        }

        public async ValueTask<int> GetSum2(int x1, int x2)
        {
            await Task.Delay(100);
            return x1 + x2;
        }

        public string GetString()
        {
            return "OK";
        }

        public string GetNullString()
        {
            return null;
        }

        public async Task Test2()
        {
            await Task.Delay(500);
        }

        public void Notify(int n)
        {
            Debug.Assert(IsNotification);
        }

        public string MakeCallback(string msg)
        {
            return Context.GetProxy<IClientTestController>().Echo(msg);
        }
        
        public async Task<string> MakeAsyncCallback(string msg)
        {
            return await Context.GetProxy<IClientTestController>().EchoAsync(msg);
        }

        public void NotifyCallback(int n)
        {
            Debug.Assert(IsNotification);

            Context.GetProxy<IClientTestController>().EchoNotification(n);
        }
    }
}
