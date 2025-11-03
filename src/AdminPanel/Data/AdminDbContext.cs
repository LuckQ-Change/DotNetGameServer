using Microsoft.EntityFrameworkCore;

namespace DotNetGameServer.AdminPanel.Data;

public class AdminDbContext : DbContext
{
    public AdminDbContext(DbContextOptions<AdminDbContext> options) : base(options)
    {
    }

    public DbSet<Resource> Resources { get; set; }
}