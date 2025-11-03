using System.ComponentModel.DataAnnotations;

namespace Core.Db;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty; // In a real app, this should be a password hash
}