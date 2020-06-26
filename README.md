# vRPC
Asynchronous full-duplex Remote Procedure Call based on WebSocket and targets high performance systems.

## Supported Runtimes
- .NET Standard 2.0+
- .NET Core 3.1+

# Basic usage
##### Client Side
```csharp
public interface IChat
{
    Task<string> EchoAsync(string message);
}

static async Task Main()
{
    var client = new VRpcClient("localhost", port: 1234, ssl: false, allowAutoConnect: true);
    var proxy = client.GetProxy<IChat>();
    string response = await proxy.EchoAsync("Hello");
}
```
##### Server side
```csharp
var listener = new VRpcListener(IPAddress.Any, 1234);
listener.Start();

[AllowAnonymous]
class ChatController : ServerController
{
    public string Echo(string msg) => msg;
}
```
### Notes 

* asynchronous and synchronous execution is supported for both parties - the client and the server
* asynchronous requests can be either Task or ValueTask
* 'Async' postfix in method name ignored
* notification requests are preferred when response from the other side is not needed
* arguments are bound by their index and not by their name
* interfaces should be pubblic unlike controllers that can be internal
* default serializers is Text.Json and Protocol Buffer
* DI support

# Notifications

A notification is similar to a request except no response will be returned.
ValueTask is most suitable for notifications.

```csharp
public interface IChat
{
    [Notification]
    ValueTask Message(string message);
}
```

# Callbacks

Server and client are considered equal parties and can make calls to each other.

##### Client Side

```csharp
class CallbackController : ClientController
{
    string MessageFromServer(string msg) => msg;
}
```
##### Server side

```csharp
public interface ICallback
{
    string MessageFromServer(string msg);
}

class ChatController : ServerController
{
    public string Echo(string msg)
    {
        return Context.GetProxy<ICallback>().MessageFromServer(msg);
    }
}
```

# Dependency Injection

Client and server behave the same about DI.

```csharp
listener.ConfigureService(s => s.AddScoped<MyService, IMyService>());

class ChatController : ServerController
{
    private readonly IMyService _myService;
    private readonly ICallback _clientCallback;

    public ChatController(IMyService myService, IProxy<ICallback> callback)
    {
        _myService = myService;
        _clientCallback = callback.Proxy; // Alternative of Context.GetProxy<>
    }
}
```

# Graceful shutdown

Graceful closing is advised for both sides. 

##### Client side
```csharp
CloseReason result = client.Shutdown(disconnectTimeout: TimeSpan.FromSeconds(2), "We're done here");
Console.WriteLine(result);
```
##### Server side.
```csharp
listener.Shutdown(TimeSpan.FromSeconds(10), "Server is stopping");
```

# Authentication

Authentication applies to the transport layer.

##### Client side
```csharp
public interface IAccount
{
    BearerToken GetToken(string userName, string password);
    string GetUserName();
}

var proxy = client.GetProxy<IAccount>();
BearerToken token = proxy.GetToken("user1", "p@$$word");
await client.SignInAsync(token.AccessToken);
string myName = proxy.GetUserName();
```
##### Server side

```csharp    
class AccountController : ServerController
{
    [AllowAnonymous]
    public ActionResult<BearerToken> GetToken(string userName, string password)
    {
        if (userName == "user1" && password == "pa$$word")
        {
            var nameClaim = new Claim(ClaimsIdentity.DefaultNameClaimType, "John Doe");
            var identity = new ClaimsIdentity("Basic");
            identity.AddClaim(nameClaim);
            BearerToken token = CreateAccessToken(new ClaimsPrincipal(identity), validTime: TimeSpan.FromDays(1));
            return token;
        }
        else
        {
            return BadRequest("Invalid username or password");
        }
    }
    public string GetUserName() => User.Identity.Name;
}
```

# Error handling
##### Client side

```csharp
try
{
    string response = chat.GetUserName(-1);
}
catch (VRpcBadRequestException ex)
{
    Console.WriteLine(ex.Message); // "Invalid userId"
}
```
##### Server side
```csharp
class ChatController : ServerController
{
    // An easier way.
    public string GetUserName(int userId)
    {
        if (userId < 0)
            throw new VRpcBadRequestException("Invalid userId");

        // ...
        return "John Doe";
    }

    // More preferable way.
    public ActionResult<string> GetUserName(int userId)
    {
        if (userId < 0)
            return BadRequest("Invalid userId");

        // ...
        return "John Doe";
    }
}
```

# Advanced Connection Establishment

To maintain a long-live connections to the server, it is better to use overload that does not cause exceptions.

```csharp
var client = new VRpcClient("localhost", port: 1234, ssl: false, allowAutoConnect: false);

// Exception-free keep-alive loop.
ThreadPool.QueueUserWorkItem(async delegate
{
    while (true)
    {
        try
        {
            ConnectResult result = await client.ConnectExAsync();

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
```

# Advanced subjects
### TCP_NODELAY

You can achieve less latency and more throughput by turning off the Nagle algorithm for specific requests.

```csharp
public interface IChat
{
    [TcpNoDelay] // For quickest sending.
    string GetUrgentStatus();
}

class ChatController : ServerController
{
    [TcpNoDelay] // For quickest reply.
    public string GetUrgentStatus() => "OK";
}
```
### ProtoBuf supportion

```csharp
class ChatController : ServerController
{
    [ProducesProtoBuf] // May speed up serialization of complex types.
    public MyClass GetData() => new MyClass();
}
```
