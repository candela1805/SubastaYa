using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Persistence;

public interface IPlaceBidRepository
{
    Task<Subasta?> GetAuctionAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Puja>> GetWinningBidsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyBidAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<Billetera?> GetWalletAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(
    CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(
        CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default);

    void AddBid(Puja puja);

    void AddLedgerTransaction(TransaccionLedger movimiento);

    void AddAuditLog(AuditoriaLog auditoria);
}
