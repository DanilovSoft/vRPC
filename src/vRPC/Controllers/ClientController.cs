using System.Diagnostics;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class ClientController : Controller
    {
        /// <summary>
        /// Контекст подключения на стороне клиента.
        /// </summary>
        public ClientSideConnection? Context { get; private set; }

        internal sealed override void BeforeInvokeController(ManagedConnection connection, ClaimsPrincipal? user)
        {
            Context = connection as ClientSideConnection;
            Debug.Assert(Context != null, "Возможно перепутаны серверный и клиентский тип контроллера.");
        }
    }
}
