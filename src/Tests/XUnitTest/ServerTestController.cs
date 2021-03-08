﻿using System;
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
        void InvalidParamsResult(string exceptionMessage);
        void TestInternalErrorThrow(string exceptionMessage);
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
        
        [JsonRpc]
        [Notification]
        Task JNotifyAsync(int n);

        [Notification]
        void NotifyCallback(int n);

        [JsonRpc]
        [Notification]
        void JNotifyCallback(int n);

        string MakeCallback(string msg);
        string MakeAsyncCallback(string msg);
        void NotExistedMethod();
        [JsonRpc]
        void JNotExistedMethod();
        [JsonRpc]
        void JTestInternalError();
    }

    [AllowAnonymous]
    internal class ServerTestController : ServerController
    {
        public void JTestInternalError()
        {
            throw new InvalidOperationException("Не удалось подключиться к БД");
        }

        public void TestInternalErrorThrow(string message)
        {
            throw new VRpcInternalErrorException(message);
        }

        public IActionResult InvalidParamsResult(string message)
        {
            return InvalidParams(message);
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

        public void JNotify(int n)
        {
            Debug.Assert(IsNotification);
        }

        public string MakeCallback(string msg)
        {
            string echo = Context.GetProxy<IClientTestController>().Echo(msg);

            Debug.Assert(echo == msg, "Эхо-сообщения не идентичны");

            return echo;
        }
        
        public async Task<string> MakeAsyncCallback(string msg)
        {
            string echo = await Context.GetProxy<IClientTestController>().EchoAsync(msg);
            
            Debug.Assert(echo == msg, "Эхо-сообщения не идентичны");

            return echo;
        }

        public void NotifyCallback(int n)
        {
            Debug.Assert(IsNotification);

            Context.GetProxy<IClientTestController>().EchoNotification(n);
        }

        public void JNotifyCallback(int n)
        {
            Debug.Assert(IsNotification);

            Context.GetProxy<IClientTestController>().JEchoNotification(n);
        }
    }
}
