using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface IRequest : IMessageToSend, IResponseAwaiter
    {
        RequestMethodMeta Method { get; }
        int Id { get; set; }
        //Task<TResult> Task { get; }
    }
}
