namespace Core
{
    /// <summary>
    /// 服务器上下文组件访问扩展方法：提供更易用的获取与校验。
    /// </summary>
    public static class ServerContextExtensions
    {
        /// <summary>
        /// 获取必需组件：若不存在则抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="ctx">服务器上下文。</param>
        /// <returns>组件实例。</returns>
        /// <exception cref="InvalidOperationException">当未注册该组件时抛出。</exception>
        public static T GetRequiredComponent<T>(this IServerContext ctx) where T : class, IServerComponent
        {
            var comp = ctx.GetComponent<T>();
            if (comp == null)
            {
                throw new InvalidOperationException($"缺少必需组件: {typeof(T).Name}");
            }
            return comp;
        }

        /// <summary>
        /// 尝试获取组件：存在返回 true 并输出实例，否则返回 false。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="ctx">服务器上下文。</param>
        /// <param name="component">输出组件实例或 null。</param>
        /// <returns>是否获取成功。</returns>
        public static bool TryGetComponent<T>(this IServerContext ctx, out T? component) where T : class, IServerComponent
        {
            component = ctx.GetComponent<T>();
            return component != null;
        }
    }
}
