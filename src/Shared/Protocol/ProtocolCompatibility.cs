namespace Shared;

/// <summary>比较客户端与目录中的协议版本。主版本相同即视为可互通。</summary>
public static class ProtocolCompatibility
{
    public static bool AreCompatible(string? clientVersion, string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(clientVersion) || string.IsNullOrWhiteSpace(serverVersion))
            return true;
        if (TryReadMajor(clientVersion, out var clientMajor) && TryReadMajor(serverVersion, out var serverMajor))
            return clientMajor == serverMajor;
        return string.Equals(clientVersion.Trim(), serverVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadMajor(string version, out int major)
    {
        major = 0;
        var end = version.IndexOfAny(['.', '-', '+']);
        var token = end < 0 ? version : version[..end];
        return int.TryParse(token, out major);
    }
}
