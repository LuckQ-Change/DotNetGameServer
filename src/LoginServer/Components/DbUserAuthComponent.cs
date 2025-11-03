using Core.Db;

namespace LoginServer;

/// <summary>
/// 基于 MySQL 的用户认证组件示例
/// 注意：演示使用明文密码比对，生产环境请使用安全的密码哈希（如 bcrypt/scrypt/argon2）
/// </summary>
public class DbUserAuthComponent : IUserAuthComponent
{
    private readonly MySqlManager _db;

    public DbUserAuthComponent(string connectionString)
    {
        _db = new MySqlManager(connectionString);
    }

    public string Name => "DbUserAuth";

    public Task InitializeAsync()
    {
        // 可在此进行数据库连接测试或表结构校验
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        // 无需显式释放（连接使用连接池且按需打开关闭）
        return Task.CompletedTask;
    }

    public async Task<AuthResult> AuthenticateAsync(string username, string password)
    {
        // 演示：从 users 表读取密码（或密码哈希）并进行比对
        const string sql = "SELECT password FROM users WHERE username=@username LIMIT 1";
        var rows = await _db.QueryAsync(sql, new Dictionary<string, object?>
        {
            ["@username"] = username
        });

        if (rows.Count == 0)
        {
            return new AuthResult(false, "用户不存在");
        }

        var row = rows[0];
        var stored = row.TryGetValue("password", out var v) ? v?.ToString() : null;

        // 这里使用明文比对，仅用于演示
        // 生产环境请替换为安全哈希比对
        var ok = !string.IsNullOrEmpty(stored) && stored == password;
        return ok ? new AuthResult(true, "登录成功") : new AuthResult(false, "用户名或密码错误");
    }
}