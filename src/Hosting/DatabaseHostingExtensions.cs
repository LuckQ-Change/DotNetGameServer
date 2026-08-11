using System.Data.Common;

using Core.Configuration;
using Core.Persistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hosting;

public static class DatabaseHostingExtensions
{
    /// <summary>
    /// 绑定 database 配置并按需注册 IGameDatabase。业务项目应先注册对应 ADO.NET Provider，
    /// 或传入 providerFactoryResolver 以接入特定数据库驱动。
    /// </summary>
    public static IServiceCollection AddGameDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, DatabaseOptions, DbProviderFactory>? providerFactoryResolver = null)
    {
        var section = configuration.GetSection("database");
        services.AddOptions<DatabaseOptions>()
            .Bind(section)
            .Validate(options => !options.Enabled || options.CommandTimeoutSeconds > 0,
                "database:commandTimeoutSeconds 必须大于 0。")
            .Validate(options => !options.Enabled || options.RollbackTimeoutSeconds > 0,
                "database:rollbackTimeoutSeconds 必须大于 0。")
            .Validate(options => !options.Enabled || options.HealthCheckTimeoutSeconds > 0,
                "database:healthCheckTimeoutSeconds 必须大于 0。")
            .ValidateOnStart();
        var options = section.Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (!options.Enabled) return services;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("database:enabled=true 时必须配置 connectionString。");
        if (providerFactoryResolver == null && string.IsNullOrWhiteSpace(options.ProviderInvariantName))
            throw new InvalidOperationException("database:enabled=true 时必须配置 providerInvariantName。");

        services.AddSingleton<AdoNetGameDatabase>(sp =>
        {
            var current = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
            var factory = providerFactoryResolver?.Invoke(sp, current)
                ?? DbProviderFactories.GetFactory(current.ProviderInvariantName);
            return new AdoNetGameDatabase(
                factory,
                current.ConnectionString,
                current.CommandTimeoutSeconds,
                current.RollbackTimeoutSeconds);
        });
        services.AddSingleton<IGameDatabase>(sp => sp.GetRequiredService<AdoNetGameDatabase>());
        services.AddSingleton<IGameDatabaseHealthProbe>(sp => sp.GetRequiredService<AdoNetGameDatabase>());
        return services;
    }
}
