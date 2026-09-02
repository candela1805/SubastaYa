using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Contracts.Auctions;

public sealed class CreateAuctionRequest
{
    [Required]
    [MaxLength(150)]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [MaxLength(2_000)]
    public string Descripcion { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ImagenUrl { get; set; }

    [Required]
    [MaxLength(100)]
    public string Categoria { get; set; } = string.Empty;

    public decimal PrecioInicial { get; set; }

    public decimal IncrementoMinimo { get; set; }

    public DateTimeOffset FechaInicioUtc { get; set; }

    public DateTimeOffset FechaFinUtc { get; set; }
}
