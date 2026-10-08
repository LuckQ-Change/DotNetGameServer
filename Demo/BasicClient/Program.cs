using BasicServer.Protocol;

using Client;
using Client.Discovery;

var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
var port = int.TryParse(args.ElementAtOrDefault(1), out var parsedPort) ? parsedPort : 5500;
var message = args.ElementAtOrDefault(2) ?? "hello";

await using var client = GameNetworkClient.CreateTcp(new ServiceEndpoint
{
    ServiceId = "demo",
    Role = "demo",
    Host = host,
    Port = port,
    Responsibilities = ["gameplay", "demo", "ping"],
    Version = "1.0.0"
});
client.ClientVersion = "1.0.0";
client.RequireCompatibleVersion = true;
client.TokenRefreshHandler = async cancellationToken =>
{
    if (!string.IsNullOrEmpty(client.AccessToken))
    {
        var refreshed = await client.RequestResultAsync(
            new RefreshSessionRequest { AccessToken = client.AccessToken },
            RefreshSessionResponse.Parser,
            cancellationToken);
        if (refreshed.HasResponse && refreshed.Response is { Success: true } session)
            return AccessTokenGrant.Create(
                session.AccessToken,
                session.UserId,
                session.ExpireUnix,
                session.RecommendedRefreshSeconds);
    }

    var retryLogin = await client.RequestResultAsync(
        new GuestLoginRequest { DisplayName = "demo" },
        GuestLoginResponse.Parser,
        cancellationToken);
    if (!retryLogin.HasResponse || retryLogin.Response is not { Success: true } guest)
        return null;
    return AccessTokenGrant.Create(
        guest.AccessToken,
        guest.UserId,
        guest.ExpireUnix,
        guest.RecommendedRefreshSeconds);
};

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
Console.WriteLine($"正在连接 {host}:{port} ...");
await client.ConnectAsync(new ServiceQuery { Responsibility = "gameplay" }, timeout.Token);

var login = await client.RequestResultAsync(
    new GuestLoginRequest { DisplayName = "demo" },
    GuestLoginResponse.Parser,
    timeout.Token);
if (!login.HasResponse || login.Response is not { Success: true } granted)
{
    Console.Error.WriteLine($"登录失败: {login.ErrorCode} {login.ErrorMessage}");
    return 1;
}

client.ApplyAccessToken(AccessTokenGrant.Create(
    granted.AccessToken,
    granted.UserId,
    granted.ExpireUnix,
    granted.RecommendedRefreshSeconds));
Console.WriteLine($"游客登录成功: {granted.UserId}");

var result = await client.RequestResultAsync(
    new PingRequest { Message = message },
    PingResponse.Parser,
    timeout.Token);

if (!result.HasResponse)
{
    Console.Error.WriteLine($"请求失败: {result.ErrorCode} {result.ErrorMessage}");
    return 1;
}

var response = result.Response!;
if (!response.Success)
{
    Console.Error.WriteLine($"业务失败: {response.Code} {response.Message}");
    return 1;
}

Console.WriteLine($"服务端响应: {response.Message}");
Console.WriteLine($"服务端 Unix 时间: {response.ServerTimeUnix}");
return 0;
