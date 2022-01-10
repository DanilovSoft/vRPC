namespace DanilovSoft.vRPC
{
    internal sealed class RequestContextScope
    {
        //public IGetProxy? ConnectionContext { get; set; }
        public RpcManagedConnection? Connection { get; set; }
    }
}
