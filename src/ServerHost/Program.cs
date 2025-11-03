using Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Core.Db;
using Microsoft.EntityFrameworkCore;
using ServerHost.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using LoginServer.Components;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        var environment = context.HostingEnvironment.EnvironmentName;
        config.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<GameDbContext>(options =>
         {
             if (context.HostingEnvironment.IsDevelopment())
             {
                 options.UseInMemoryDatabase("GameDb");
             }
             else
             {
                 options.UseNpgsql(context.Configuration.GetConnectionString("DefaultConnection"));
             }
         });

        services.AddScoped<IUserAuthComponent, DbUserAuthComponent>();

        services.AddHostedService<GameServerService>();

        services.AddHealthChecks().AddCheck<ServerHealthCheck>("server_health_check");
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .Build();

// Seed the database in development environment
{
    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var env = services.GetRequiredService<IHostEnvironment>();
    if (env.IsDevelopment())
    {
        var dbContext = services.GetRequiredService<GameDbContext>();
        if (!dbContext.Users.Any())
        {
            // Assuming the User model is accessible, as per the user's intent.
            // A 'using' for the User model's namespace and System.Linq may be required.
            dbContext.Users.Add(new Core.Db.User { Username = "test", Password = "password" });
            dbContext.SaveChanges();
        }
    }
}

await host.RunAsync();

public class GameServerService : IHostedService
{
    private readonly ILogger<GameServerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _hostEnvironment;
    private BaseGameServer? _server;

    public GameServerService(ILogger<GameServerService> logger, IConfiguration configuration, IServiceProvider serviceProvider, IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _hostEnvironment = hostEnvironment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var service = _configuration["service"] ?? Environment.GetEnvironmentVariable("SERVICE") ?? "directory";
        var port = _configuration.GetValue<int?>("port") ??
                   (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5500);

        _server = service.ToLowerInvariant() switch
        {
            "directory" => new DirectoryServer.DirectoryGameServer(_serviceProvider.GetRequiredService<ILogger<DirectoryServer.DirectoryGameServer>>()),
            "login" => new LoginServer.LoginGameServer(_serviceProvider.GetRequiredService<ILogger<LoginServer.LoginGameServer>>(), _serviceProvider.GetRequiredService<IUserAuthComponent>()),
            "resource" => new ResourceServer.ResourceGameServer(_serviceProvider.GetRequiredService<ILogger<ResourceServer.ResourceGameServer>>()),
            "battle" => new BattleServer.BattleGameServer(_serviceProvider.GetRequiredService<ILogger<BattleServer.BattleGameServer>>()),
            "logic" => new LogicServer.LogicGameServer(_serviceProvider.GetRequiredService<ILogger<LogicServer.LogicGameServer>>()),
            _ => throw new ArgumentException($"未知服务类型: {service}")
        };

        await _server.StartAsync(port);
        _logger.LogInformation($"[Host] {_server.ServerName} listening on port {port}, env={_hostEnvironment.EnvironmentName}");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server != null)
        {
            await _server.StopAsync();
        }
    }
}