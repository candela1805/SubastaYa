using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Application.Features.Bids.Queries.GetBidHistory;
using Xunit;
using SubastaYa.Domain.Models;

namespace SubastaYa.API.Tests.CQRS;

public sealed class GetBidHistoryQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsHistory_WhenAuctionExists()
    {
        var subastaId = Guid.NewGuid();
        var query = new GetBidHistoryQuery(subastaId);

        var history = new List<BidHistoryResult>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Monto = 1500m,
                Pseudonimo = "12345678",
                FechaUtc = DateTimeOffset.UtcNow
            }
        };

        var repository = new FakeBidReadRepository
        {
            AuctionExists = true,
            History = history
        };

        var handler = new GetBidHistoryQueryHandler(repository);

        var result = await handler.HandleAsync(query);

        Assert.Same(history, result);
        Assert.Equal(subastaId, repository.LastAuctionId);
        Assert.Equal(1, repository.ExistsCalls);
        Assert.Equal(1, repository.HistoryCalls);
    }

    [Fact]
    public async Task HandleAsync_Throws_WhenAuctionDoesNotExist()
    {
        var query = new GetBidHistoryQuery(Guid.NewGuid());

        var repository = new FakeBidReadRepository
        {
            AuctionExists = false
        };

        var handler = new GetBidHistoryQueryHandler(repository);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.HandleAsync(query));

        Assert.Equal(1, repository.ExistsCalls);
        Assert.Equal(0, repository.HistoryCalls);
    }

    private sealed class FakeBidReadRepository : IBidReadRepository
    {
        public bool AuctionExists { get; set; }

        public IReadOnlyList<BidHistoryResult> History { get; set; }
            = Array.Empty<BidHistoryResult>();

        public Guid LastAuctionId { get; private set; }

        public int ExistsCalls { get; private set; }

        public int HistoryCalls { get; private set; }

        public Task<bool> AuctionExistsAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            LastAuctionId = subastaId;
            ExistsCalls++;

            return Task.FromResult(AuctionExists);
        }

        public Task<IReadOnlyList<BidHistoryResult>> GetHistoryAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            LastAuctionId = subastaId;
            HistoryCalls++;

            return Task.FromResult(History);
        }

        public Task<Subasta?> GetAuctionAsync(
    Guid subastaId,
    CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Subasta?>(null);
        }

        public Task<IReadOnlyList<Guid>> GetWinningBidUserIdsAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
        }

        public Task<bool> HasAnyBidAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> HasUserBidAsync(
            Guid subastaId,
            Guid usuarioId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
