namespace DotNetGameServer;

/// <summary>
/// 框架 Protobuf 响应实现统一的成败契约。成功时信封错误码仍为 0，
/// 业务字段留在载荷里。
/// </summary>
public sealed partial class HeartbeatResponse : Shared.IServerResponse
{
    bool Shared.IServerResponse.Success => Accepted;

    int Shared.IServerResponse.Code => Accepted ? 0 : 401;

    string Shared.IServerResponse.Message => Message;
}

public sealed partial class ServerQueryResponse : Shared.IServerResponse
{
    bool Shared.IServerResponse.Success => true;

    int Shared.IServerResponse.Code => 0;

    string Shared.IServerResponse.Message => string.Empty;
}

public sealed partial class ServerUnregisterResponse : Shared.IServerResponse;

public sealed partial class ClientHeartbeatResponse : Shared.IServerResponse;
