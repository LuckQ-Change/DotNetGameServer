using Core;
using DotNetGameServer;
using Google.Protobuf;

namespace ResourceServer;

/// <summary>
/// 获取资源包处理器
/// </summary>
public class GetBundleMessageHandler : IStrongMessageHandler<GetBundleRequest, GetBundleResponse>
{
    public async Task<GetBundleResponse?> HandleAsync(GetBundleRequest request, IServerContext context)
    {
        var repo = context.GetComponent<IResourceRepositoryComponent>();
        if (repo == null)
        {
            context.Log("资源仓库组件未找到");
            return new GetBundleResponse { Success = false, Message = "资源仓库组件未找到" };
        }

        var result = await repo.GetBundleAsync(request.Name, request.Version);
        var response = new GetBundleResponse
        {
            Success = result.Success,
            Message = result.Message,
            Hash = result.Hash,
            Size = result.Size,
            Content = result.Content != null ? ByteString.CopyFrom(result.Content) : ByteString.Empty
        };

        context.Log($"获取AB: {request.Name}@{request.Version}, size={response.Size}, success={response.Success}");
        return response;
    }
}