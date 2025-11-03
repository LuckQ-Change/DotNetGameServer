namespace ResourceServer;

public class InMemoryResourceRepository : IResourceRepositoryComponent
{
    private readonly List<BundleInfo> _bundles = new();
    public string Name => "InMemoryResourceRepository";

    public Task InitializeAsync()
    {
        // 初始化示例数据
        _bundles.Clear();
        _bundles.Add(new BundleInfo("characters.ab", "hash-abc123", 1024, ""));
        _bundles.Add(new BundleInfo("maps.ab", "hash-def456", 2048, ""));
        _bundles.Add(new BundleInfo("ui.ab", "hash-ghi789", 512, ""));
        return Task.CompletedTask;
    }

    public Task DestroyAsync() => Task.CompletedTask;

    public Task<VersionManifest> GetManifestAsync(string platform)
    {
        // 简单示例：平台不区分，返回固定版本与清单
        var manifest = new VersionManifest("1.0.0", _bundles);
        return Task.FromResult(manifest);
    }

    public Task<BundleContent> GetBundleAsync(string name, string version)
    {
        var info = _bundles.FirstOrDefault(b => b.Name == name);
        if (info is null)
        {
            return Task.FromResult(new BundleContent(false, "bundle not found", null, string.Empty, 0));
        }

        // 演示：返回伪造内容（真实项目中应从文件系统或对象存储读取）
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{name}:{version}:dummy-content");
        return Task.FromResult(new BundleContent(true, "ok", bytes, info.Hash, bytes.Length));
    }
}