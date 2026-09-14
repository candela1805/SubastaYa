using SubastaYa.API.Models;

namespace SubastaYa.API.Contracts.Bids;

public sealed class BidRoomStateResponse
{
    public Guid SubastaId { get; set; }

    public decimal PrecioActual { get; set; }

    public decimal IncrementoMinimo { get; set; }

    public decimal PujaMinimaSiguiente { get; set; }

    public DateTimeOffset FechaFinUtc { get; set; }

    public EstadoSubasta EstadoSubasta { get; set; }

    public string EstadoPostor { get; set; } = string.Empty;
}
