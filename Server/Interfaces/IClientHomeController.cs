using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using vRPC;

namespace Server
{
    [ControllerContract("Home")]
    public interface IClientHomeController
    {
        Task SetClientIdAsync(int id);
        Task<string> SayHelloAsync(string s);
    }
}
