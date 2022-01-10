namespace DanilovSoft.vRPC
{
    //[DebuggerDisplay(@"\{BadRequestResult: {_message}\}")]
    //public class BadRequestResult : StatusCodeResult
    //{
    //    private const StatusCode DefaultStatusCode = StatusCode.BadRequest;
    //    private readonly string _message;

    //    public BadRequestResult(string message) : base (DefaultStatusCode)
    //    {
    //        _message = message;
    //    }

    //    private protected sealed override void FinalWriteResult(ref ActionContext context)
    //    {
    //        context.StatusCode = DefaultStatusCode;
    //        context.ResponseBuffer.WriteStringBinary(_message);
    //    }
    //}
}