using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Core.Db;

public class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        // This is a bit of a hack to get the connection string from appsettings.json
        // in the ServerHost project. A better solution might be to use user secrets
        // or environment variables.
        // 保留设计时工厂，当前不读取配置文件，避免未使用变量警告。

        var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();

        // Use In-Memory database for design-time tools
        optionsBuilder.UseInMemoryDatabase("GameDb");

        return new GameDbContext(optionsBuilder.Options);
    }
}