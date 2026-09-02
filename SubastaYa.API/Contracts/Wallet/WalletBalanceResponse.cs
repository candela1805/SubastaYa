namespace SubastaYa.API.Contracts.Wallet;

public sealed class WalletBalanceResponse
{
    public decimal Total { get; set; }
    public decimal Retenido { get; set; }
    public decimal Disponible { get; set; }

}
