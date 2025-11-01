using Core;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace ResourceServer;

public record BundleInfo(string Name, string Hash, int Size, string Url);
public record BundleContent(bool Success, string Message, byte[]? Content, string Hash, int Size);
public record VersionManifest(string LatestVersion, IReadOnlyList<BundleInfo> Bundles);

public interface IResourceRepositoryComponent : IServerComponent
{
    Task<VersionManifest> GetManifestAsync(string platform);
    Task<BundleContent> GetBundleAsync(string name, string version);
}

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

public class FileSystemResourceRepository : IResourceRepositoryComponent
{
    private readonly string _root;
    private readonly string _versionFile;
    public string Name => "FileSystemResourceRepository";

    public FileSystemResourceRepository(string root)
    {
        _root = root;
        _versionFile = Path.Combine(_root, "version.txt");
    }

    public Task InitializeAsync()
    {
        // 无需预加载，按需扫描目录
        return Task.CompletedTask;
    }

    public Task DestroyAsync() => Task.CompletedTask;

    public Task<VersionManifest> GetManifestAsync(string platform)
    {
        var effectiveRoot = _root;
        if (!string.IsNullOrWhiteSpace(platform))
        {
            var platformRoot = Path.Combine(_root, platform);
            if (Directory.Exists(platformRoot)) effectiveRoot = platformRoot;
        }

        var bundles = new List<BundleInfo>();
        if (Directory.Exists(effectiveRoot))
        {
            foreach (var file in Directory.EnumerateFiles(effectiveRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                var size = (int)new FileInfo(file).Length;
                var hash = ComputeSha256(file);
                bundles.Add(new BundleInfo(name, hash, size, ""));
            }
        }

        var version = ReadVersion(effectiveRoot);
        return Task.FromResult(new VersionManifest(version, bundles));
    }

    public async Task<BundleContent> GetBundleAsync(string name, string version)
    {
        var path = Path.Combine(_root, name);
        if (!File.Exists(path))
        {
            return new BundleContent(false, "bundle not found", null, string.Empty, 0);
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var hash = ComputeSha256(path);
        return new BundleContent(true, "ok", bytes, hash, bytes.Length);
    }

    private string ReadVersion(string root)
    {
        try
        {
            var vf = Path.Combine(root, "version.txt");
            if (File.Exists(vf))
            {
                var v = File.ReadAllText(vf, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { /* ignore */ }
        return "1.0.0";
    }

    private static string ComputeSha256(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        var hashBytes = sha.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}