namespace SubastaYa.Application.Features.Bids.DTOs;

public sealed record BidHistoryResult
{
    public Guid Id { get; init; }
    public decimal Monto { get; init; }
    public string Pseudonimo { get; init; } = string.Empty;
    public DateTimeOffset FechaUtc { get; init; }
}
