using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Features.Bids.DTOs;

public sealed record BidRoomStateResult
{
    public Guid SubastaId { get; init; }

    public decimal PrecioActual { get; init; }

    public decimal IncrementoMinimo { get; init; }

    public decimal PujaMinimaSiguiente { get; init; }

    public DateTimeOffset FechaFinUtc { get; init; }

    public EstadoSubasta EstadoSubasta { get; init; }

    public string EstadoPostor { get; init; } = string.Empty;
}
