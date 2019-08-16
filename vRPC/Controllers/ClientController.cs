namespace vRPC
{
    public abstract class ClientController : Controller
    {
        /// <summary>
        /// Контекст подключения на стороне клиента.
        /// </summary>
        public ServerContext Context { get; internal set; }
    }
}
