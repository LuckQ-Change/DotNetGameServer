using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Core.Db;

public class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        // 保留设计时工厂，当前不读取配置文件，避免未使用变量警告。

        var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();

        optionsBuilder.UseInMemoryDatabase("GameDb");

        return new GameDbContext(optionsBuilder.Options);
    }
}
