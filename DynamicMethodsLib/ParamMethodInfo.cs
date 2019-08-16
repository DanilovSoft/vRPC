using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DynamicMethodsLib
{
    internal readonly struct ParamMethodInfo
    {
        public readonly ParameterInfo Param;
        public readonly int Index;

        public ParamMethodInfo(ParameterInfo param, int index)
        {
            Param = param;
            Index = index;
        }
    }
}
