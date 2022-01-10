using DanilovSoft.vRPC.Context;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface INotification : IMessageToSend
    {
        ValueTask WaitNotificationAsync();
    }
}
