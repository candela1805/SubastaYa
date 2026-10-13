using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Persistence;

public interface IAuctionRepository
{
    Task<PagedAuctionResult> SearchAsync(string? search, decimal? minPrice, decimal? maxPrice, string? category, EstadoSubasta? state, string? order, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AuctionResult>> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BidActivityResult>> GetBidActivitiesAsync(Guid userId, CancellationToken cancellationToken);
    Task<Subasta?> GetOwnedTrackedAsync(Guid auctionId, Guid sellerId, CancellationToken cancellationToken);
    Task<Subasta?> GetTrackedAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<bool> HasBidsAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<bool> HasSettlementAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<bool> HasAuditAsync(Guid auctionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetScheduledIdsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetExpiredIdsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
    void Add(Subasta auction);
    void Remove(Subasta auction);
    void AddAudit(AuditoriaLog audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
