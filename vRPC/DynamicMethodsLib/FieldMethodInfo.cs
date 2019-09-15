using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DynamicMethodsLib
{
    internal readonly struct FieldMethodInfo
    {
        public string FieldName { get; }
        public MethodInfo MethodInfo { get; }

        public FieldMethodInfo(string fieldName, MethodInfo methodInfo)
        {
            FieldName = fieldName;
            MethodInfo = methodInfo;
        }
    }
}
