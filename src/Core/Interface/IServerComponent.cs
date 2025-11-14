namespace Core;

/// <summary>
/// 服务器组件接口，用于ECS架构中的组件系统
/// </summary>
public interface IServerComponent
{
    /// <summary>
    /// 组件名称，用于日志标识与诊断。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 初始化组件，准备运行期资源与依赖。
    /// </summary>
    /// <returns>表示异步完成的任务。</returns>
    public Task InitializeAsync();

    /// <summary>
    /// 销毁组件，释放运行期资源。
    /// </summary>
    /// <returns>表示异步完成的任务。</returns>
    public Task DestroyAsync();
}

