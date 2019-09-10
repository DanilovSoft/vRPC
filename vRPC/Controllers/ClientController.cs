namespace DanilovSoft.vRPC
{
    public abstract class ClientController : Controller
    {
        /// <summary>
        /// Контекст подключения на стороне клиента.
        /// </summary>
        public RpcClient Context { get; internal set; }
    }
}
