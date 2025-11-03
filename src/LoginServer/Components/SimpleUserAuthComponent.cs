namespace LoginServer;

/// <summary>
/// 简单用户认证组件实现
/// </summary>
public class SimpleUserAuthComponent : IUserAuthComponent
{
    private readonly Dictionary<string, string> _users = new()
    {
        { "admin", "123456" },
        { "user1", "password1" },
        { "test", "test123" }
    };

    public string Name => "SimpleUserAuth";

    public Task InitializeAsync()
    {
        // 初始化用户数据，可以从数据库加载
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        // 清理资源
        return Task.CompletedTask;
    }

    public Task<AuthResult> AuthenticateAsync(string username, string password)
    {
        if (_users.TryGetValue(username, out var storedPassword) && storedPassword == password)
        {
            return Task.FromResult(new AuthResult(true, "登录成功"));
        }

        return Task.FromResult(new AuthResult(false, "用户名或密码错误"));
    }
}