using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Security.Cryptography;
using System;
using DotNetGameServer.AdminPanel.Data;
using Microsoft.EntityFrameworkCore;

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
options.DefaultFileNames.Add("resource-management.html");
app.UseDefaultFiles(options);
app.UseStaticFiles();



// 定义要监控的服务器健康检查端点列表
var serverHealthEndpoints = new Dictionary<string, string>
{
    { "Directory Server", "http://localhost:5101/health" },
    { "Login Server", "http://localhost:5102/health" },
    { "Resource Server", "http://localhost:5103/health" },
    { "Battle Server", "http://localhost:5104/health" },
    { "Logic Server", "http://localhost:5105/health" }
};

app.MapGet("/api/server-status", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient();

    var tasks = serverHealthEndpoints.Select(async endpoint =>
    {
        try
        {
            var response = await client.GetAsync(endpoint.Value);
            var content = await response.Content.ReadAsStringAsync();
            var serverReport = JsonDocument.Parse(content);
            
            serverReport.RootElement.TryGetProperty("status", out var statusElement);
            serverReport.RootElement.TryGetProperty("checks", out var checksElement);

            return new ServerStatusResult
            {
                Name = endpoint.Key,
                Status = statusElement.GetString() ?? "Unknown",
                Checks = checksElement.Clone(), // Clone to avoid lifetime issues with JsonDocument
            };
        }
        catch (Exception ex)
        {
            return new ServerStatusResult
            {
                Name = endpoint.Key,
                Status = "Unhealthy",
                Description = "无法连接到健康检查端点或处理时发生错误。",
                Exception = ex.Message
            };
        }
    });

    var results = await Task.WhenAll(tasks);
    var overallStatus = results.Any(r => r.Status != "Healthy") ? "Unhealthy" : "Healthy";

    var finalReport = new
    {
        status = overallStatus,
        checks = results
    };

    return Results.Json(finalReport);
});

app.MapPost("/api/resource/upload", async (IFormFile file, AdminDbContext dbContext, ILogger<Program> logger) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    var tempPath = Path.GetTempFileName();
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


app.Run();

// Define a concrete class for the result to avoid type inference issues
public class ServerStatusResult
{
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public object? Checks { get; set; }
    public string? Description { get; set; }
    public string? Exception { get; set; }
}
