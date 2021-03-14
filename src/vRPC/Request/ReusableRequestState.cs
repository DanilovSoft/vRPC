namespace DanilovSoft.vRPC
{
    internal enum ReusableRequestState
    {
        None = 0,
        Reset = 1,
        ReadyToSend = 2,
        Sending = 3,
        WaitingResponse = 4,
        GotResponse = 5,
        GotErrorResponse = 6,
    }
}
