using System.Reflection;

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
