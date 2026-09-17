namespace SubastaYa.API.Contracts.Wallet;

public sealed class WalletTransactionResponse
{
    public Guid Id { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public DateTimeOffset FechaUtc { get; set; }
}
