using System.Reflection;

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
