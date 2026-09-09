namespace SubastaYa.API.Contracts.Bids;
public class BidHistoryResponse
{
    public Guid Id { get; set; }
    public decimal Monto { get; set; }
    public string Pseudonimo { get; set; } = string.Empty;
    public DateTimeOffset FechaUtc  { get; set; }
}
