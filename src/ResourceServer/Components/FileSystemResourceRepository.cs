using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace ResourceServer;

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