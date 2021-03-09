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
    [AllowAnonymous]
    internal class ServerTestController : RpcController
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
            Debug.Assert(IsNotificationRequest);
        }

        public void JNotify(int n)
        {
            Debug.Assert(IsNotificationRequest);
        }

        public string MakeCallback(string msg)
        {
            string echo = Connection.GetProxy<IClientTestController>().Echo(msg);

            Debug.Assert(echo == msg, "Эхо-сообщения не идентичны");

            return echo;
        }
        
        public async Task<string> MakeAsyncCallback(string msg)
        {
            string echo = await Connection.GetProxy<IClientTestController>().EchoAsync(msg);
            
            Debug.Assert(echo == msg, "Эхо-сообщения не идентичны");

            return echo;
        }

        public void NotifyCallback(int n)
        {
            Debug.Assert(IsNotificationRequest);

            Connection.GetProxy<IClientTestController>().EchoNotification(n);
        }

        public void JNotifyCallback(int n)
        {
            Debug.Assert(IsNotificationRequest);

            Connection.GetProxy<IClientTestController>().JEchoNotification(n);
        }
    }
}
