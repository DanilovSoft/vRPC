using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DynamicMethodsLib
{
    internal readonly struct FieldMethodInfo
    {
        public readonly string FieldName;
        public readonly MethodInfo MethodInfo;

        public FieldMethodInfo(string fieldName, MethodInfo methodInfo)
        {
            FieldName = fieldName;
            MethodInfo = methodInfo;
        }
    }
}
