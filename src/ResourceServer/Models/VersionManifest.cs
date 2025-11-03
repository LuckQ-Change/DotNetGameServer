namespace ResourceServer;

public record VersionManifest(string LatestVersion, IReadOnlyList<BundleInfo> Bundles);