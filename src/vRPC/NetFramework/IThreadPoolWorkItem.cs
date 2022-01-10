
/* Необъединенное слияние из проекта "DanilovSoft.vRPC (netstandard2.0)"
До:
using System;
using System.Collections.Generic;
using System.Text;

#if NETSTANDARD2_0 || NET472
После:
#if NETSTANDARD2_0 || NET472
*/

/* Необъединенное слияние из проекта "DanilovSoft.vRPC (net472)"
До:
using System;
using System.Collections.Generic;
using System.Text;

#if NETSTANDARD2_0 || NET472
После:
#if NETSTANDARD2_0 || NET472
*/

#if NETSTANDARD2_0 || NET472

namespace System.Threading
{
    internal interface IThreadPoolWorkItem
    {
        void Execute();
    }
}
#endif
