using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace DynamicMethodsLib
{
    internal readonly struct OutRefArg
    {
        public readonly ParameterInfo Param;
        public readonly int Index;
        public readonly LocalBuilder Local;

        public OutRefArg(ParameterInfo param, int index, LocalBuilder localBuilder)
        {
            Param = param;
            Index = index;
            Local = localBuilder;
        }
    }
}
