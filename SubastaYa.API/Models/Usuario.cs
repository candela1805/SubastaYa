using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models;
public sealed class Usuario
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset FechaRegistro { get; set; } = DateTimeOffset.UtcNow;

    public Billetera? Billetera { get; set; }
}
