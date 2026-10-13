using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Bids.DTOs;

namespace SubastaYa.Application.Features.Bids.Queries.GetBidHistory;

public sealed class GetBidHistoryQueryHandler
    : IQueryHandler<GetBidHistoryQuery, IReadOnlyList<BidHistoryResult>>
{
    private readonly IBidReadRepository _repository;

    public GetBidHistoryQueryHandler(IBidReadRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<BidHistoryResult>> HandleAsync(
        GetBidHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var existeSubasta = await _repository.AuctionExistsAsync(
            query.SubastaId,
            cancellationToken);

        if (!existeSubasta)
        {
            throw new KeyNotFoundException("La subasta no existe.");
        }

        return await _repository.GetHistoryAsync(
            query.SubastaId,
            cancellationToken);
    }
}
