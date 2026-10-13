using SubastaYa.Application.Features.Wallet.DTOs;
using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Persistence;

public interface IWalletRepository
{
    Task<WalletBalanceResult?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken);
    Task<Billetera?> GetTrackedWalletAsync(Guid userId, CancellationToken cancellationToken);
    Task<TransaccionLedger?> GetLedgerTransactionByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WalletTransactionResult>?> GetTransactionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<RetainedFundsResult?> GetRetainedFundsAsync(Guid userId, CancellationToken cancellationToken);
    Task BeginTransactionAsync(CancellationToken cancellationToken);
    void AddLedgerTransaction(TransaccionLedger transaction);
    void AddAuditLog(AuditoriaLog audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}
