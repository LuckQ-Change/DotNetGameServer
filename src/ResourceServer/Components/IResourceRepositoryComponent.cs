using Core;

namespace ResourceServer;

public interface IResourceRepositoryComponent : IServerComponent
{
    Task<VersionManifest> GetManifestAsync(string platform);
    Task<BundleContent> GetBundleAsync(string name, string version);
}