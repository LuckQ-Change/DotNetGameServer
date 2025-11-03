using Core;
using DotNetGameServer;
using Microsoft.Extensions.Logging;

namespace LoginServer;

/// <summary>
/// 登录服务器实现
/// </summary>
public class LoginGameServer : BaseGameServer
{
    public LoginGameServer(Action<string> log) : base("Login", log)
    {
        // 注册登录消息处理器（强类型路由）
        RegisterStrongMessageHandler<LoginRequest, LoginResponse>(new LoginMessageHandler());

        // LoginServer 仅负责登录认证，不注册业务类处理器

        // 优先使用环境变量提供的 MySQL 连接进行数据库认证
        var conn = BuildMySqlConnectionFromEnv();
        if (!string.IsNullOrEmpty(conn))
        {
            AddComponent<IUserAuthComponent>(new DbUserAuthComponent(conn));
            Log("使用 MySQL 数据库用户认证组件");
        }
        else
        {
            // 回退到内置的简单用户认证
            AddComponent<IUserAuthComponent>(new SimpleUserAuthComponent());
            Log("未检测到 MySQL 配置，使用内置用户认证组件");
        }

        // 服务内心跳组件
        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "login",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }

    public LoginGameServer(ILogger logger) : base("Login", logger)
    {
        // 注册登录消息处理器（强类型路由）
        RegisterStrongMessageHandler<LoginRequest, LoginResponse>(new LoginMessageHandler());

        // LoginServer 仅负责登录认证，不注册业务类处理器

        var conn = BuildMySqlConnectionFromEnv();
        if (!string.IsNullOrEmpty(conn))
        {
            AddComponent<IUserAuthComponent>(new DbUserAuthComponent(conn));
            Log("使用 MySQL 数据库用户认证组件");
        }
        else
        {
            AddComponent<IUserAuthComponent>(new SimpleUserAuthComponent());
            Log("未检测到 MySQL 配置，使用内置用户认证组件");
        }

        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "login",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }

    /// <summary>
    /// 创建登录服务器实例
    /// </summary>
    public static LoginGameServer Create(Action<string> log)
    {
        return new LoginGameServer(log);
    }

    private static string? BuildMySqlConnectionFromEnv()
    {
        // 支持三种方式：
        // 0) 从 config.json(mysql) 读取
        var fromConfig = Core.Config.SimpleConfigLoader.LoadMySqlConnectionString();
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig;

        // 1) 直接提供完整连接串：MYSQL_CONN
        // 2) 分项提供：MYSQL_HOST, MYSQL_PORT, MYSQL_USER, MYSQL_PASSWORD, MYSQL_DB
        var direct = Environment.GetEnvironmentVariable("MYSQL_CONN");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var host = Environment.GetEnvironmentVariable("MYSQL_HOST");
        var portStr = Environment.GetEnvironmentVariable("MYSQL_PORT");
        var user = Environment.GetEnvironmentVariable("MYSQL_USER");
        var pass = Environment.GetEnvironmentVariable("MYSQL_PASSWORD");
        var db = Environment.GetEnvironmentVariable("MYSQL_DB");

        if (!string.IsNullOrWhiteSpace(host) &&
            !string.IsNullOrWhiteSpace(portStr) &&
            !string.IsNullOrWhiteSpace(user) &&
            !string.IsNullOrWhiteSpace(pass) &&
            !string.IsNullOrWhiteSpace(db) &&
            int.TryParse(portStr, out var port))
        {
            return Core.Db.MySqlManager.BuildConnectionString(host!, port, user!, pass!, db!);
        }

        return null;
    }
}