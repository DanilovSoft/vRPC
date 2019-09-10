using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    [ControllerContract("Home")]
    public interface IClientHomeController
    {
        Task SetClientIdAsync(int id);
        Task<string> SayHelloAsync(string s);
    }
}
