using Core;
using DotNetGameServer;
using Google.Protobuf;

namespace ResourceServer;

/// <summary>
/// 版本查询处理器
/// </summary>
public class QueryVersionMessageHandler : IStrongMessageHandler<QueryVersionRequest, QueryVersionResponse>
{
    public async Task<QueryVersionResponse?> HandleAsync(QueryVersionRequest request, IServerContext context)
    {
        var repo = context.GetComponent<IResourceRepositoryComponent>();
        if (repo == null)
        {
            context.Log("资源仓库组件未找到");
            return new QueryVersionResponse { UpdateAvailable = false, LatestVersion = "" };
        }

        var manifest = await repo.GetManifestAsync(request.Platform);
        var response = new QueryVersionResponse
        {
            // 不考虑客户端版本差异，统一不提示更新
            UpdateAvailable = false,
            LatestVersion = manifest.LatestVersion,
        };
        var signer = context.GetComponent<UrlSignerComponent>();
        var hb = context.GetComponent<Core.Components.HeartbeatClientComponent>();
        var sid = hb?.TryGetCurrentServerId();
        foreach (var b in manifest.Bundles)
        {
            var info = new ResourceBundleInfo
            {
                Name = b.Name,
                Hash = b.Hash,
                Size = b.Size,
                Url = b.Url
            };
            if (signer != null && !string.IsNullOrWhiteSpace(info.Url) && !string.IsNullOrWhiteSpace(sid))
            {
                info.Url = signer.Sign(info.Url, sid!, TimeSpan.FromMinutes(10));
            }
            response.Bundles.Add(info);
        }

        context.Log($"版本查询: latest={response.LatestVersion}, bundles={response.Bundles.Count}");
        return response;
    }
}