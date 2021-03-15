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
        Task<string?> GetNullStringAsync();
        string GetString();
        string? GetNullString();

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
        [JsonRpc]
        object GetSomeObject();
    }
}
