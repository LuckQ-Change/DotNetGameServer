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
        // 在这里配置您的实体和它们之间的关系
        // 例如: modelBuilder.Entity<Player>().ToTable("players");
    }
}