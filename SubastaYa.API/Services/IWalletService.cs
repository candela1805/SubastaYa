using SubastaYa.API.Contracts.Wallet;

namespace SubastaYa.API.Services;

public interface IWalletService
{
    Task<WalletBalanceResponse?> ObtenerSaldoAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task<WalletBalanceResponse?> DepositarAsync(
        Guid usuarioId,
        decimal monto,
        CancellationToken cancellationToken = default);
}