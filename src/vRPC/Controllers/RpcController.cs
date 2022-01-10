namespace DanilovSoft.vRPC
{
    public abstract class RpcController
    {
        private RequestContext? _requestContext;
        public bool IsNotificationRequest => !_requestContext!.IsResponseRequired;
        public RpcManagedConnection Connection => _requestContext!.Connection;

        // Должен быть пустой конструктор для наследников.
        public RpcController()
        {

        }

        //internal abstract void BeforeInvokeController(VrpcManagedConnection connection, ClaimsPrincipal? user);

        internal void BeforeInvokeController(RequestContext requestContext)
        {
            _requestContext = requestContext;
        }

        protected static InvalidParamsResult InvalidParams(string errorMessage)
        {
            return new InvalidParamsResult(errorMessage);
        }

        protected static OkResult Ok()
        {
            return new OkResult();
        }

        protected static OkObjectResult Ok(object value)
        {
            return new OkObjectResult(value);
        }
    }
}
