namespace SubastaYa.Application.Features.Bids.DTOs;

public sealed record PlaceBidResult
{
    public Guid Id { get; init; }
    public Guid SubastaId { get; init; }
    public decimal Monto { get; init; }
    public string Pseudonimo { get; init; } = string.Empty;
    public DateTimeOffset FechaUtc { get; init; }
    public decimal PrecioActual { get; init; }
    public DateTimeOffset FechaFinUtc { get; init; }
    public bool SubastaExtendida { get; init; }
    public string Estado { get; init; } = string.Empty;
    public decimal IncrementoMinimo { get; init; }
    public decimal PujaMinimaSiguiente { get; init; }
}
