using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class VNotification : INotification
    {
        public VNotification(ManagedConnection context, RequestMethodMeta method, object[] args)
        {

        }

        public ValueTask WaitNotificationAsync()
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
