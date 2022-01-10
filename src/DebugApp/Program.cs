using DanilovSoft.vRPC;

class Program
{
    static async Task Main()
    {
        var client = new VRpcClient("localhost", port: 1234, ssl: false, allowAutoConnect: false);

        // Exception-free keep-alive loop.
        ThreadPool.QueueUserWorkItem(async delegate
        {
            while (true)
            {
                try
                {
                    var result = await client.ConnectExAsync();

                    if (result.State == ConnectionState.Connected)
                    {
                        var closeReason = await client.Completion;
                        Console.WriteLine(closeReason);
                    }
                    else if (result.State == ConnectionState.SocketError)
                    {
                        Console.WriteLine(result.SocketError);
                        await Task.Delay(30_000);
                    }
                    else if (result.State == ConnectionState.ShutdownRequest)
                    {
                        Console.WriteLine("Another thread requested Shutdown");
                        return;
                    }
                }
                catch (VRpcConnectException ex)
                // An exception may occur in rare cases.
                {
                    await Task.Delay(30_000);
                }
            }
        });

        Console.ReadKey();
        var result = client.Shutdown(TimeSpan.FromSeconds(2), "Остановлено пользователем");
        Console.ReadKey();
    }
}
