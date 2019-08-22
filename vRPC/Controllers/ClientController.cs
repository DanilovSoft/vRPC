namespace vRPC
{
    public abstract class ClientController : Controller
    {
        /// <summary>
        /// Контекст подключения на стороне клиента.
        /// </summary>
        public Client Context { get; internal set; }
    }
}
