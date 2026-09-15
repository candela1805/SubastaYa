using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models;

public class AuditoriaLog
{
    public Guid Id { get; set; }
    public Guid SubastaId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TipoEvento { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Detalle { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ClaveIdempotencia { get; set; }

    public DateTimeOffset FechaUtc { get; set; } = DateTimeOffset.UtcNow;
    public Subasta Subasta { get; set; } = null!;
}

