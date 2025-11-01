using Core;

namespace LoginServer;

/// <summary>
/// 登录服务器实现
/// </summary>
public class LoginGameServer : BaseGameServer
{
    public LoginGameServer(Action<string> log) : base("Login", log)
    {
        // 注册登录消息处理器
        RegisterMessageHandler(new LoginMessageHandler());
        
        // 添加用户认证组件
        AddComponent<IUserAuthComponent>(new SimpleUserAuthComponent());
    }

    /// <summary>
    /// 创建登录服务器实例
    /// </summary>
    public static LoginGameServer Create(Action<string> log)
    {
        return new LoginGameServer(log);
    }
}