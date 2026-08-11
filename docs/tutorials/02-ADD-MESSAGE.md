# 第二课：增加一条业务消息

下面以“查询角色名字”为例，完整走一遍协议到处理器的流程。

## 1. 定义协议

在业务项目的 `.proto` 文件追加消息。已经发布的字段编号不能修改或复用。

```proto
message GetPlayerRequest {
  int64 player_id = 1;
}

message GetPlayerResponse {
  bool success = 1;
  int32 code = 2;
  string message = 3;
  string player_name = 4;
}
```

保存后执行 `dotnet build`，`Grpc.Tools` 会自动生成 C# 类型，不要手工编辑 `obj/` 中的生成文件。

## 2. 编写处理器

```csharp
public sealed class GetPlayerHandler
    : IMessageHandler<GetPlayerRequest, GetPlayerResponse>
{
    public Task<GetPlayerResponse?> HandleAsync(
        GetPlayerRequest request,
        IServerContext context)
    {
        if (request.PlayerId <= 0)
        {
            return Task.FromResult<GetPlayerResponse?>(new GetPlayerResponse
            {
                Success = false,
                Code = 400,
                Message = "invalid_player_id"
            });
        }

        return Task.FromResult<GetPlayerResponse?>(new GetPlayerResponse
        {
            Success = true,
            PlayerName = $"player-{request.PlayerId}"
        });
    }
}
```

处理器需要访问数据库、缓存或房间管理器时，把长期资源实现为 `IServerComponent`，再通过 `context.GetComponent<T>()` 获取。异步操作必须传递 `context.CancellationToken`，这样请求超时和停服才能及时取消。

## 3. 注册处理器

在自己的 `BaseGameServer` 派生类构造函数中注册：

```csharp
RegisterClientMessageHandler(new GetPlayerHandler());
```

客户端消息使用 `RegisterClientMessageHandler`；只有可信服务器之间的消息才使用 `RegisterInternalMessageHandler`。两者不能混用。

## 4. 错误约定

- 非法 Protobuf：框架返回 `400 / invalid_request_payload`；
- 未携带所需令牌：返回 `401 / authentication_required`；
- 通道错误：返回 `403 / communication_channel_forbidden`；
- 未注册类型：返回 `404 / unknown_message_type`；
- 处理超时：返回 `408 / request_timeout`；
- 处理器未捕获异常：框架记录完整异常，对外只返回 `500 / internal_error`。

不要把异常堆栈、数据库错误或密钥写入响应消息。

