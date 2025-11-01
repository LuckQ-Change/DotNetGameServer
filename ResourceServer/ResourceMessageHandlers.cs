using Core;
using DotNetGameServer;
using Google.Protobuf;

namespace ResourceServer;

/// <summary>
/// 版本查询处理器
/// </summary>
public class QueryVersionMessageHandler : IMessageHandler
{
    public string MessageType => "QueryVersionRequest";

    public async Task<byte[]?> HandleAsync(byte[] payload, IServerContext context)
    {
        var request = QueryVersionRequest.Parser.ParseFrom(payload);
        var repo = context.GetComponent<IResourceRepositoryComponent>();
        if (repo == null)
        {
            context.Log("资源仓库组件未找到");
            return null;
        }

        var manifest = await repo.GetManifestAsync(request.Platform);
        var response = new QueryVersionResponse
        {
            UpdateAvailable = request.ClientVersion != manifest.LatestVersion,
            LatestVersion = manifest.LatestVersion,
        };
        foreach (var b in manifest.Bundles)
        {
            response.Bundles.Add(new ResourceBundleInfo
            {
                Name = b.Name,
                Hash = b.Hash,
                Size = b.Size,
                Url = b.Url
            });
        }

        context.Log($"版本查询: client={request.ClientVersion}, latest={response.LatestVersion}, update={response.UpdateAvailable}");
        return response.ToByteArray();
    }
}

/// <summary>
/// 获取资源包处理器
/// </summary>
public class GetBundleMessageHandler : IMessageHandler
{
    public string MessageType => "GetBundleRequest";

    public async Task<byte[]?> HandleAsync(byte[] payload, IServerContext context)
    {
        var request = GetBundleRequest.Parser.ParseFrom(payload);
        var repo = context.GetComponent<IResourceRepositoryComponent>();
        if (repo == null)
        {
            context.Log("资源仓库组件未找到");
            return null;
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
        return response.ToByteArray();
    }
}