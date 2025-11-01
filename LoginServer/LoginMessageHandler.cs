using Core;
using DotNetGameServer;
using Google.Protobuf;

namespace LoginServer;

/// <summary>
/// 登录请求消息处理器
/// </summary>
public class LoginMessageHandler : IMessageHandler
{
    public string MessageType => "LoginRequest";

    public async Task<byte[]?> HandleAsync(byte[] payload, IServerContext context)
    {
        var request = LoginRequest.Parser.ParseFrom(payload);
        
        // 获取用户验证组件
        var authComponent = context.GetComponent<IUserAuthComponent>();
        
        bool success;
        string message;
        
        if (authComponent != null)
        {
            var result = await authComponent.AuthenticateAsync(request.Username, request.Password);
            success = result.Success;
            message = result.Message;
        }
        else
        {
            // 默认验证逻辑
            success = request.Username == "admin" && request.Password == "123456";
            message = success ? "登录成功" : "用户名或密码错误";
        }
        
        context.Log($"处理登录请求: {request.Username}, 结果={success}");
        
        var response = new LoginResponse 
        { 
            Success = success, 
            Message = message 
        };
        
        return response.ToByteArray();
    }
}

/// <summary>
/// 用户认证组件接口
/// </summary>
public interface IUserAuthComponent : IServerComponent
{
    Task<AuthResult> AuthenticateAsync(string username, string password);
}

/// <summary>
/// 认证结果
/// </summary>
public record AuthResult(bool Success, string Message);

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