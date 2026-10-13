using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Persistence;

public interface IBidReadRepository
{
    Task<bool> AuctionExistsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BidHistoryResult>> GetHistoryAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<Subasta?> GetAuctionAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetWinningBidUserIdsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyBidAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<bool> HasUserBidAsync(
        Guid subastaId,
        Guid usuarioId,
        CancellationToken cancellationToken = default);
}