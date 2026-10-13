using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Persistence;

public interface IAuctionClosingRepository
{
    Task BeginTransactionAsync(CancellationToken cancellationToken);
    Task<Subasta?> GetAuctionAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<bool> HasSettlementAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Puja>> GetWinningBidsAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<bool> HasBidsAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<decimal>> GetBidAmountsAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<Billetera?> GetWalletAsync(Guid userId, CancellationToken cancellationToken);
    void AddSettlement(LiquidacionSubasta settlement);
    void AddLedgerTransactions(params TransaccionLedger[] transactions);
    void AddAudit(AuditoriaLog audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
    Task<bool> IsAlreadyProcessedAsync(Guid auctionId, CancellationToken cancellationToken);
}
