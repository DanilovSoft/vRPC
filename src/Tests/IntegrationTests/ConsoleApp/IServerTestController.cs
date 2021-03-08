using DanilovSoft.vRPC;
using System.Threading.Tasks;

namespace InternalConsoleApp
{
    [ControllerContract("Test")]
    public interface IServerTestController
    {
        void TestException(string exceptionMessage);
        [JsonRpc]
        void TestExceptionThrow(string exceptionMessage);
        void TestDelay();
        Task Test2Async();
        [JsonRpc]
        int GetSum(int x1, int x2);
        int GetSum2(int x1, int x2);
        [JsonRpc]
        Task<int> GetSumAsync(int x, int y);
        Task<string> GetNullStringAsync();
        string GetString();
        string GetNullString();

        [Notification]
        void Notify(int n);
        [Notification]
        ValueTask NotifyAsync(int n);
        [Notification]
        void NotifyCallback(int n);

        string MakeCallback(string msg);
        string MakeAsyncCallback(string msg);
    }
}
