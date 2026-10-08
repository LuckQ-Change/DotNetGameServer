namespace Core;

/// <summary>
/// 统一错误码规范化：将语义状态或错误字符串映射到标准整数错误码。
/// </summary>
public static class ErrorCodeMapper
{
    public static int Map(string? statusOrCode)
    {
        if (string.IsNullOrWhiteSpace(statusOrCode)) return 400;
        var s = statusOrCode.Trim().ToUpperInvariant();
        return s switch
        {
            "OK" => 0,
            "ERROR" => 500,
            "INTERNAL" => 500,
            "AUTH_REQUIRED" => 401,
            "UNAUTHORIZED" => 401,
            "FORBIDDEN" => 403,
            "BAD_REQUEST" => 400,
            "NOT_FOUND" => 404,
            "RATE_LIMIT" => 429,
            "TOO_MANY_REQUESTS" => 429,
            "SERVICE_NOT_READY" => 503,
            "ECS_NOT_FOUND" => 503,
            "UNKNOWN_TYPE" => 400,
            _ => int.TryParse(s, out var num) ? num : 400
        };
    }
}
