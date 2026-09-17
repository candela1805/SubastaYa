using SubastaYa.API.Models;

namespace SubastaYa.API.Contracts.Auctions;

public sealed class BidActivityResponse
{
    public Guid SubastaId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? ImagenUrl { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public decimal PrecioInicial { get; set; }
    public decimal MiOferta { get; set; }
    public decimal OfertaActual { get; set; }
    public decimal IncrementoMinimo { get; set; }
    public DateTimeOffset FechaInicioUtc { get; set; }
    public DateTimeOffset FechaFinUtc { get; set; }
    public EstadoSubasta Estado { get; set; }
    public string Resultado { get; set; } = string.Empty;
}
