using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Features.Auctions.Queries;

public sealed record SearchAuctionsQuery(string? Search, decimal? MinPrice, decimal? MaxPrice, string? Category, EstadoSubasta? State, string? Order, int Page, int PageSize);
public sealed record GetAuctionCategoriesQuery;
public sealed record GetSellerAuctionsQuery(Guid SellerId);
public sealed record GetUserBidActivitiesQuery(Guid UserId);
public sealed record GetAuctionCycleCandidatesQuery(DateTimeOffset Now, int BatchSize);
public sealed record AuctionCycleCandidatesResult(IReadOnlyList<Guid> ScheduledIds, IReadOnlyList<Guid> ExpiredIds);

public sealed class SearchAuctionsQueryHandler : IQueryHandler<SearchAuctionsQuery, PagedAuctionResult>
{
    private readonly IAuctionRepository _repository;
    public SearchAuctionsQueryHandler(IAuctionRepository repository) => _repository = repository;
    public Task<PagedAuctionResult> HandleAsync(SearchAuctionsQuery query, CancellationToken cancellationToken = default) =>
        _repository.SearchAsync(query.Search, query.MinPrice, query.MaxPrice, query.Category, query.State, query.Order, query.Page, query.PageSize, cancellationToken);
}

public sealed class GetAuctionCategoriesQueryHandler : IQueryHandler<GetAuctionCategoriesQuery, IReadOnlyList<string>>
{
    private readonly IAuctionRepository _repository;
    public GetAuctionCategoriesQueryHandler(IAuctionRepository repository) => _repository = repository;
    public Task<IReadOnlyList<string>> HandleAsync(GetAuctionCategoriesQuery query, CancellationToken cancellationToken = default) => _repository.GetCategoriesAsync(cancellationToken);
}

public sealed class GetSellerAuctionsQueryHandler : IQueryHandler<GetSellerAuctionsQuery, IReadOnlyList<AuctionResult>>
{
    private readonly IAuctionRepository _repository;
    public GetSellerAuctionsQueryHandler(IAuctionRepository repository) => _repository = repository;
    public Task<IReadOnlyList<AuctionResult>> HandleAsync(GetSellerAuctionsQuery query, CancellationToken cancellationToken = default) => _repository.GetBySellerAsync(query.SellerId, cancellationToken);
}

public sealed class GetUserBidActivitiesQueryHandler : IQueryHandler<GetUserBidActivitiesQuery, IReadOnlyList<BidActivityResult>>
{
    private readonly IAuctionRepository _repository;
    public GetUserBidActivitiesQueryHandler(IAuctionRepository repository) => _repository = repository;
    public Task<IReadOnlyList<BidActivityResult>> HandleAsync(GetUserBidActivitiesQuery query, CancellationToken cancellationToken = default) => _repository.GetBidActivitiesAsync(query.UserId, cancellationToken);
}

public sealed class GetAuctionCycleCandidatesQueryHandler : IQueryHandler<GetAuctionCycleCandidatesQuery, AuctionCycleCandidatesResult>
{
    private readonly IAuctionRepository _repository;
    public GetAuctionCycleCandidatesQueryHandler(IAuctionRepository repository) => _repository = repository;
    public async Task<AuctionCycleCandidatesResult> HandleAsync(GetAuctionCycleCandidatesQuery query, CancellationToken cancellationToken = default) =>
        new(await _repository.GetScheduledIdsAsync(query.Now, query.BatchSize, cancellationToken), await _repository.GetExpiredIdsAsync(query.Now, query.BatchSize, cancellationToken));
}
