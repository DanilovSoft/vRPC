namespace vRPC
{
    public sealed class OkResult : ActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.Ok;

        public OkResult() : base(DefaultStatusCode)
        {

        }

        public override void ExecuteResult(ActionContext context)
        {
            context.StatusCode = StatusCode;
        }
    }
}