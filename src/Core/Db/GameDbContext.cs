using Microsoft.EntityFrameworkCore;

namespace Core.Db;

public class GameDbContext : DbContext
{
    public DbSet<User> Users { get; set; }

    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 在这里配置实体的映射关系（表名、索引、外键等），使用 Fluent API 进行配置。
        // 示例说明：可在此处设置实体对应的表名与约束，但请根据实际模型编写具体配置。
    }
}