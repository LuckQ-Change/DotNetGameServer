using System;
using System.Collections.Generic;
using Core;
using Microsoft.Extensions.Logging;

namespace DirectoryServer.Tests;

/// <summary>
/// 简易的测试用服务器上下文实现，提供组件注册与日志方法。
/// </summary>
public class TestServerContext : IServerContext
{
    private readonly Dictionary<Type, IServerComponent> _components = new();

    public string ServerName { get; init; } = "DirectoryTests";
    public string AccessToken { get; set; } = string.Empty;
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public string RemoteAddress { get; init; } = "127.0.0.1";
    public AuthInfo? Auth { get; set; } = null;

    public void Log(string message)
    {
        // 简化：测试中输出到控制台即可
        Console.WriteLine(message);
    }

    public void Log(string message, LogLevel level)
    {
        Console.WriteLine($"[{level}] {message}");
    }

    public T? GetComponent<T>() where T : class, IServerComponent
    {
        if (_components.TryGetValue(typeof(T), out var comp)) return comp as T;
        return null;
    }

    /// <summary>
    /// 注册组件以便处理器在上下文中检索。
    /// </summary>
    public void AddComponent<T>(T component) where T : class, IServerComponent
    {
        _components[typeof(T)] = component;
    }
}