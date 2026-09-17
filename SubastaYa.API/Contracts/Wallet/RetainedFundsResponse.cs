namespace SubastaYa.API.Contracts.Wallet;

public sealed class RetainedFundsResponse
{
    public decimal TotalRetenido { get; set; }
    public decimal TotalIdentificado { get; set; }
    public decimal Diferencia { get; set; }
    public IReadOnlyList<RetainedFundItemResponse> Items { get; set; } =
        Array.Empty<RetainedFundItemResponse>();
}

public sealed class RetainedFundItemResponse
{
    public Guid SubastaId { get; set; }
    public string Subasta { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateTimeOffset FechaUtc { get; set; }
    public string Estado { get; set; } = string.Empty;
}
