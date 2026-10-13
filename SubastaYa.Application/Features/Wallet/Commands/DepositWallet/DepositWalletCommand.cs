namespace SubastaYa.Application.Features.Wallet.Commands.DepositWallet;

public sealed record DepositWalletCommand(
    Guid UserId,
    decimal Amount,
    string? IdempotencyKey = null);
