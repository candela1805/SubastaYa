namespace SubastaYa.Application.Features.Wallet.DTOs;

public sealed record WalletBalanceResult(
    decimal Total,
    decimal Retenido,
    decimal Disponible);

public sealed record WalletTransactionResult(
    Guid Id,
    string Tipo,
    decimal Monto,
    string Descripcion,
    DateTimeOffset FechaUtc);

public sealed record RetainedFundItemResult(
    Guid SubastaId,
    string Subasta,
    decimal Monto,
    DateTimeOffset FechaUtc,
    string Estado);

public sealed record RetainedFundsResult(
    decimal TotalRetenido,
    decimal TotalIdentificado,
    decimal Diferencia,
    IReadOnlyList<RetainedFundItemResult> Items);
