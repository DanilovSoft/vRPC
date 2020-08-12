using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;

namespace ServerApp
{
    [AllowAnonymous]
    public class PingController : ServerController
    {
        public void Ping()
        {
            Console.WriteLine("Ping");

            Context.GetProxy<ITest>().Message("Hello from Server");
        }
    }

    public interface ITest
    {
        void Message(string msg);
    }
}
