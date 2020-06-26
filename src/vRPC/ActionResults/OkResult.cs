namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Пустой результат с кодом Ok.
    /// </summary>
    public class OkResult : ActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.Ok;

        public OkResult() : base(DefaultStatusCode)
        {

        }

        private protected sealed override void FinalExecuteResult(ActionContext context)
        {
            context.StatusCode = StatusCode;
        }
    }
}