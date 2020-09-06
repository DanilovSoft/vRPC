using System;
using System.Collections.Generic;
using System.Text;

#if NETSTANDARD2_0 || NET472

namespace System.Threading
{
    internal interface IThreadPoolWorkItem
    {
        void Execute();
    }
}
#endif
