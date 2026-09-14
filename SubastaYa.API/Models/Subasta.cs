using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models;

public sealed class Subasta
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? VendedorId { get; set; }

    public Usuario? Vendedor { get; set; }

    [Required]
    [MaxLength(150)]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [MaxLength(2_000)]
    public string? Descripcion { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ImagenUrl { get; set; }

    [Required]
    [MaxLength(100)]
    public string Categoria {  get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioInicial { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioActual { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal IncrementoMinimo {  get; set; }

    public DateTimeOffset FechaInicioUtc { get; set; }

    public DateTimeOffset FechaFinUtc { get; set; }

    public EstadoSubasta Estado {  get; set; }

    // SQL Server genera un nuevo valor en cada INSERT o UPDATE.
    [Timestamp]
    public byte[] Version { get; set; } = Array.Empty<byte>();
}
