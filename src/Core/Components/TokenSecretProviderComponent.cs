namespace Core.Components;

public interface ITokenSecretProvider : IServerComponent
{
    public string GetSecret();
    public IEnumerable<string> GetVerifySecrets();
}

public class TokenSecretProviderComponent : ITokenSecretProvider
{
    private readonly string _secret;

    public string Name => "TokenSecretProvider";

    public TokenSecretProviderComponent(string? tokenSecret = null)
    {
        _secret = !string.IsNullOrWhiteSpace(tokenSecret) ? tokenSecret : "dev-secret";
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DestroyAsync() => Task.CompletedTask;

    public string GetSecret() => _secret;

    public IEnumerable<string> GetVerifySecrets()
    {
        yield return _secret;
    }
}
