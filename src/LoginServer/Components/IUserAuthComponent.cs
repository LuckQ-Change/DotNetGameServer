using Core;

namespace LoginServer.Components;

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