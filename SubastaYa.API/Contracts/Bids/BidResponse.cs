namespace SubastaYa.API.Contracts.Bids;

public class BidResponse
{
    public Guid Id { get; set; }
    public Guid SubastaId { get; set; }
    public decimal Monto { get; set; }
    public string Pseudonimo { get; set; } = string.Empty;
    public DateTimeOffset FechaUtc { get; set; }
    public decimal PrecioActual { get; set; }
    public DateTimeOffset FechaFinUtc { get; set; }
    public bool SubastaExtendida { get; set; }
    public string Estado { get; set; } = string.Empty;
    public decimal IncrementoMinimo { get; set; }
    public decimal PujaMinimaSiguiente { get; set; }
}
