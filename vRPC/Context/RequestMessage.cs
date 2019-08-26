using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    internal sealed class RequestMessage : Message
    {
        /// <summary>
        /// Конструктор запроса.
        /// </summary>
        public RequestMessage(string actionName, Arg[] args)
        {
            ActionName = actionName;
            Args = new JToken[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Args[i] = args[i].Value;
            }
        }
    }
}
