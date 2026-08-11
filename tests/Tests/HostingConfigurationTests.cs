using Hosting;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace Tests;

public sealed class HostingConfigurationTests
{
    [Fact]
    public void EnvironmentAndCommandLine_OverrideJsonInDocumentedOrder()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-game-server-config-{Guid.NewGuid():N}");
        const string key = "DGS_AUDIT_PRIORITY";
        var previousValue = Environment.GetEnvironmentVariable(key);
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "appsettings.json"), $$"""
        {
          "{{key}}": "json"
        }
        """);
        Environment.SetEnvironmentVariable(key, "environment");

        try
        {
            using var configuration = new ConfigurationManager();
            configuration
                .SetBasePath(tempDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables();
            GameServerHost.AddCommandLineOverrides(configuration, [$"--{key}=command"]);

            Assert.Equal("command", configuration[key]);

            using var environmentConfiguration = new ConfigurationManager();
            environmentConfiguration
                .SetBasePath(tempDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables();
            GameServerHost.AddCommandLineOverrides(environmentConfiguration, []);
            Assert.Equal("environment", environmentConfiguration[key]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previousValue);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
