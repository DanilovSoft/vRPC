using DanilovSoft.vRPC;

namespace InternalConsoleApp
{
    [JsonRpc]
    public interface ITest
    {
        string Echo(string msg, int tel);
    }
}
