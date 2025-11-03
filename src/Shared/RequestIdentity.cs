namespace Shared;

/// <summary>
/// 统一鉴权：请求身份接口。实现该接口的请求将用于标准化提取用户标识，避免反射。
/// </summary>
public interface IUserIdentifiedRequest
{
    /// <summary>
    /// 返回该请求关联的用户ID；不存在时返回 null。
    /// </summary>
    string? GetUserIdOrNull();
}