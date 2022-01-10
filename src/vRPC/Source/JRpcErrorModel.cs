using System.Runtime.InteropServices;

namespace DanilovSoft.vRPC
{
    [StructLayout(LayoutKind.Auto)]
    internal struct JRpcErrorModel
    {
        public StatusCode Code;
        public string? Message;

        public static bool operator ==(in JRpcErrorModel left, in JRpcErrorModel right)
        {
            return left.Code == right.Code;
        }

        public static bool operator !=(in JRpcErrorModel left, in JRpcErrorModel right)
        {
            return left.Code != right.Code;
        }
    }
}
