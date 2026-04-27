using Core.Db;
using Microsoft.EntityFrameworkCore;

namespace LoginServer.Components;

/// <summary>
/// 基于 Entity Framework Core 的用户认证组件。
/// 注意：演示使用明文密码比对，生产环境请使用安全的密码哈希（如 bcrypt/scrypt/argon2）。
/// </summary>
public class DbUserAuthComponent(GameDbContext dbContext) : IUserAuthComponent
{
    public string Name => "DbUserAuth";

    public Task InitializeAsync()
    {
        // EF Core 的 DbContext 是作用域生命周期，无需手动初始化或销毁连接。
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<AuthResult> AuthenticateAsync(string username, string password)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user == null)
        {
            return new AuthResult(false, "用户不存在");
        }

        var passwordMatches = user.Password == password;

        return passwordMatches
            ? new AuthResult(true, "登录成功")
            : new AuthResult(false, "用户名或密码错误");
    }
}
