using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Contracts.Auctions;

public sealed class UpdateAuctionRequest
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

    public DateTimeOffset FechaFinUtc { get; set; }
}
