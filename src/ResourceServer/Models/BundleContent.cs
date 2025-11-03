namespace ResourceServer;

public record BundleContent(bool Success, string Message, byte[]? Content, string Hash, int Size);