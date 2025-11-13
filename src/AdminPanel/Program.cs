using System.Text.Json;
using System.Security.Cryptography;
using DotNetGameServer.AdminPanel; // 为访问 HealthStatus 和 ServerStatusResult 所在命名空间
using DotNetGameServer.AdminPanel.Data;
using Microsoft.EntityFrameworkCore;

const string HealthStatus = "Healthy";
const string UnhealthyStatus = "Unhealthy";
const string DegradedStatus = "Degraded";

var builder = WebApplication.CreateBuilder(args);

// 注册 IHttpClientFactory
builder.Services.AddHttpClient();

// Add DbContext
builder.Services.AddDbContext<AdminDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

var options = new DefaultFilesOptions();
options.DefaultFileNames.Clear();
// 将首页改为管理界面（服务器监控）
options.DefaultFileNames.Add("monitoring/status.html");
app.UseDefaultFiles(options);
app.UseStaticFiles();

// 重定向根路径到监控页面
app.MapGet("/", () => Results.Redirect("/monitoring/status.html"));

app.MapPost("/api/resource/upload", async (IFormFile file, AdminDbContext dbContext, ILogger<Program> logger) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    var tempDir = Path.Combine(Path.GetTempPath(), "adminpanel_uploads");
    Directory.CreateDirectory(tempDir);
    var tempPath = Path.Combine(tempDir, Path.GetRandomFileName());
    string hashString = string.Empty;

    try
    {
        using (var fileStream = new FileStream(tempPath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        using (var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
        using (var sha256 = SHA256.Create())
        {
            var hash = await sha256.ComputeHashAsync(fileStream);
            hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        var existingResource = await dbContext.Resources.FirstOrDefaultAsync(r => r.Hash == hashString);
        if (existingResource != null)
        {
            logger.LogInformation($"File '{file.FileName}' with hash {hashString} already exists. Skipping upload.");
            return Results.Ok(new { resourceId = existingResource.Id, fileName = existingResource.FileName, size = existingResource.Size, path = existingResource.Path });
        }

        var permanentStoragePath = Path.Combine("wwwroot", "resources", hashString);
        Directory.CreateDirectory(Path.GetDirectoryName(permanentStoragePath)!);
        File.Move(tempPath, permanentStoragePath);

        var resource = new DotNetGameServer.AdminPanel.Data.Resource
        {
            FileName = file.FileName,
            Hash = hashString,
            Size = file.Length,
            Path = $"/resources/{hashString}",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        logger.LogInformation($"File '{file.FileName}' uploaded successfully. Stored at: {permanentStoragePath}");

        return Results.Ok(new { resourceId = resource.Id, fileName = resource.FileName, size = resource.Size, path = resource.Path });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error uploading file.");
        return Results.Problem("An error occurred while uploading the file.");
    }
    finally
    {
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
});


// 服务器状态API端点 - 自动服务发现
app.MapGet("/api/server-status", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    try
    {
        var httpClient = httpClientFactory.CreateClient();
        var checks = new List<object>();
        
        // 定义服务配置（基于start_all_servers.bat的端口分配）
        var services = new[]
        {
            new { name = "Directory Server", port = 5101, gamePort = 5500 },
            new { name = "Login Server", port = 5102, gamePort = 5501 },
            new { name = "Battle Server", port = 5103, gamePort = 5502 },
            new { name = "Logic Server", port = 5104, gamePort = 5503 }
        };
        
        // 检查每个服务的状态
        foreach (var service in services)
        {
            try
            {
                // 尝试连接健康检查端口
                var healthResponse = await httpClient.GetAsync($"http://localhost:{service.port}/health", 
                    new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
                
                if (healthResponse.IsSuccessStatusCode)
                {
                    var healthData = await healthResponse.Content.ReadFromJsonAsync<JsonElement>();
                    checks.Add(new
                    {
                        name = service.name,
                        status = HealthStatus,
                        description = $"服务运行正常 (游戏端口: {service.gamePort})",
                        exception = (string?)null,
                        data = new { healthPort = service.port, gamePort = service.gamePort, healthData }
                    });
                }
                else
                {
                    checks.Add(new
                    {
                        name = service.name,
                        status = UnhealthyStatus,
                        description = $"健康检查返回错误: {healthResponse.StatusCode}",
                        exception = (string?)null,
                        data = new { healthPort = service.port, gamePort = service.gamePort }
                    });
                }
            }
            catch (Exception ex)
            {
                // 服务未运行或连接失败
                checks.Add(new
                {
                    name = service.name,
                    status = "Unhealthy",
                    description = "服务未运行或连接失败",
                    exception = ex.Message,
                    data = new { healthPort = service.port, gamePort = service.gamePort }
                });
            }
        }
        
        // 添加Admin Panel自身状态
        checks.Add(new
        {
            name = "Admin Panel",
            status = HealthStatus,
            description = "管理面板运行正常",
            exception = (string?)null,
            data = new { uptime = Environment.TickCount64 / 1000, version = "1.0.0" }
        });
        
        var overallStatus = checks.Exists(c => c.GetType().GetProperty("status")?.GetValue(c)?.ToString() != HealthStatus) ? DegradedStatus : HealthStatus;
        
        var serverStatus = new
        {
            status = overallStatus,
            checks = checks.ToArray()
        };
        
        return Results.Ok(serverStatus);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error getting server status");
        return Results.Problem("Error getting server status");
    }
});

// SSE服务器状态流端点 - 自动服务发现
app.MapGet("/api/server-status/stream", async (HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");
    
    var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    
    // 定义服务配置
    var services = new[]
    {
        new { name = "Directory Server", port = 5101, gamePort = 5500 },
        new { name = "Login Server", port = 5102, gamePort = 5501 },
        new { name = "Battle Server", port = 5103, gamePort = 5502 },
        new { name = "Logic Server", port = 5104, gamePort = 5503 }
    };
    
    try
    {
        while (!context.RequestAborted.IsCancellationRequested)
        {
            var checks = new List<object>();
            
            // 检查每个服务的状态
            foreach (var service in services)
            {
                try
                {
                    var healthResponse = await httpClient.GetAsync($"http://localhost:{service.port}/health", 
                        new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
                    
                    if (healthResponse.IsSuccessStatusCode)
                    {
                        var healthData = await healthResponse.Content.ReadFromJsonAsync<JsonElement>();
                        checks.Add(new
                        {
                            name = service.name,
                            status = HealthStatus,
                            description = $"服务运行正常 (游戏端口: {service.gamePort})",
                            exception = (string?)null,
                            data = new { healthPort = service.port, gamePort = service.gamePort, healthData }
                        });
                    }
                    else
                    {
                        checks.Add(new
                        {
                            name = service.name,
                            status = UnhealthyStatus,
                            description = $"健康检查返回错误: {healthResponse.StatusCode}",
                            exception = (string?)null,
                            data = new { healthPort = service.port, gamePort = service.gamePort }
                        });
                    }
                }
                catch (Exception ex)
                {
                    checks.Add(new
                    {
                        name = service.name,
                        status = UnhealthyStatus,
                        description = "服务未运行或连接失败",
                        exception = ex.Message,
                        data = new { healthPort = service.port, gamePort = service.gamePort }
                    });
                }
            }
            
            // 添加Admin Panel自身状态
            checks.Add(new
            {
                name = "Admin Panel",
                status = HealthStatus,
                description = "管理面板运行正常",
                exception = (string?)null,
                data = new { uptime = Environment.TickCount64 / 1000, version = "1.0.0" }
            });
            
            var overallStatus = checks.Exists(c => c.GetType().GetProperty("status")?.GetValue(c)?.ToString() != HealthStatus) ? DegradedStatus : HealthStatus;
            
            var serverStatus = new
            {
                status = overallStatus,
                checks = checks.ToArray()
            };
            
            var data = $"data: {System.Text.Json.JsonSerializer.Serialize(serverStatus)}\n\n";
            await context.Response.WriteAsync(data);
            await context.Response.Body.FlushAsync();
            
            // 每3秒发送一次更新
            await Task.Delay(3000, context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("SSE connection closed");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SSE error");
    }
});

app.Run();

