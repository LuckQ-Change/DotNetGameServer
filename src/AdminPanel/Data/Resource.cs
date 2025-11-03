using System.ComponentModel.DataAnnotations;

namespace DotNetGameServer.AdminPanel.Data;

public class Resource
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [StringLength(64)] // SHA256 hash
    public string Hash { get; set; } = string.Empty;

    [Required]
    public long Size { get; set; }

    [Required]
    [StringLength(1024)]
    public string Path { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}