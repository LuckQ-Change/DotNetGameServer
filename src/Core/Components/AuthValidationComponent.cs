using Core.Config;
using Shared;

namespace Core.Components;

/// <summary>
/// 统一鉴权组件：校验 Envelope.token 是否有效且绑定到当前服务器与用户。
/// </summary>
public class AuthValidationComponent : IServerComponent
{
    public string Name => "AuthValidation";

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    /// <summary>
    /// 校验令牌：需要用户ID以完成绑定校验。
    /// </summary>
    /// <param name="context">服务器上下文（提供 AccessToken 与组件访问）。</param>
    /// <param name="userId">用户ID，用于校验令牌绑定。</param>
    /// <param name="error">错误信息（校验失败）。</param>
    /// <returns>校验通过返回 true。</returns>
    public bool TryValidate(IServerContext context, string userId, out string error)
    {
        error = string.Empty;
        var token = context.AccessToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            error = "缺少令牌";
            return false;
        }

        var hb = context.GetComponent<HeartbeatClientComponent>();
        var serverId = hb?.TryGetCurrentServerId();
        if (string.IsNullOrWhiteSpace(serverId))
        {
            error = "服务未就绪";
            return false;
        }

        var secretProvider = context.GetComponent<ITokenSecretProvider>();
        var secret = secretProvider?.GetSecret() ?? SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory) ?? "dev-secret";
        if (!TokenSigner.TryVerify(token, userId, serverId!, secret, out var _, out var _))
        {
            error = "令牌校验失败";
            return false;
        }
        return true;
    }
}