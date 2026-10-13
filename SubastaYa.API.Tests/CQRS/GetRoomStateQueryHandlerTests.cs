using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Exceptions;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Application.Features.Bids.Queries.GetRoomState;
using SubastaYa.Domain.Models;
using Xunit;

namespace SubastaYa.API.Tests.CQRS;

public class GetRoomStateQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenAuctionDoesNotExist_ThrowsNotFound()
    {
        var repository = new FakeBidReadRepository();
        var handler = new GetRoomStateQueryHandler(repository);

        var query = new GetRoomStateQuery(Guid.NewGuid(), Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<BidNotFoundException>(
            () => handler.HandleAsync(query));

        Assert.Equal("AUCTION_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenAuctionHasNoBids_ReturnsSinPuja()
    {
        var subasta = CreateAuction();
        var repository = new FakeBidReadRepository
        {
            Auction = subasta
        };

        var handler = new GetRoomStateQueryHandler(repository);

        var result = await handler.HandleAsync(
            new GetRoomStateQuery(subasta.Id, Guid.NewGuid()));

        Assert.Equal("SinPuja", result.EstadoPostor);
        Assert.Equal(1600m, result.PujaMinimaSiguiente);
        Assert.Equal(subasta.Id, result.SubastaId);
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsWinning_ReturnsLiderando()
    {
        var subasta = CreateAuction();
        var usuarioId = Guid.NewGuid();

        var repository = new FakeBidReadRepository
        {
            Auction = subasta,
            WinningBidUserIds = new[] { usuarioId },
            HasAnyBid = true
        };

        var handler = new GetRoomStateQueryHandler(repository);

        var result = await handler.HandleAsync(
            new GetRoomStateQuery(subasta.Id, usuarioId));

        Assert.Equal("Liderando", result.EstadoPostor);
    }

    [Fact]
    public async Task HandleAsync_WhenUserWasOutbid_ReturnsSuperado()
    {
        var subasta = CreateAuction();

        var usuarioId = Guid.NewGuid();
        var ganadorId = Guid.NewGuid();

        var repository = new FakeBidReadRepository
        {
            Auction = subasta,
            WinningBidUserIds = new[] { ganadorId },
            HasAnyBid = true,
            HasUserBid = true
        };

        var handler = new GetRoomStateQueryHandler(repository);

        var result = await handler.HandleAsync(
            new GetRoomStateQuery(subasta.Id, usuarioId));

        Assert.Equal("Superado", result.EstadoPostor);
    }

    [Fact]
    public async Task HandleAsync_WhenAuctionHasMultipleWinners_ThrowsConflict()
    {
        var subasta = CreateAuction();

        var repository = new FakeBidReadRepository
        {
            Auction = subasta,
            WinningBidUserIds = new[]
            {
                Guid.NewGuid(),
                Guid.NewGuid()
            },
            HasAnyBid = true
        };

        var handler = new GetRoomStateQueryHandler(repository);

        var exception = await Assert.ThrowsAsync<BidStateConflictException>(
            () => handler.HandleAsync(
                new GetRoomStateQuery(subasta.Id, Guid.NewGuid())));

        Assert.Equal("BID_STATE_INCONSISTENT", exception.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenAuctionHasBidsButNoWinner_ThrowsConflict()
    {
        var subasta = CreateAuction();

        var repository = new FakeBidReadRepository
        {
            Auction = subasta,
            HasAnyBid = true
        };

        var handler = new GetRoomStateQueryHandler(repository);

        var exception = await Assert.ThrowsAsync<BidStateConflictException>(
            () => handler.HandleAsync(
                new GetRoomStateQuery(subasta.Id, Guid.NewGuid())));

        Assert.Equal("BID_STATE_INCONSISTENT", exception.Code);
    }

    private static Subasta CreateAuction()
    {
        return new Subasta
        {
            Id = Guid.NewGuid(),
            PrecioActual = 1500m,
            IncrementoMinimo = 100m,
            FechaFinUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    private sealed class FakeBidReadRepository : IBidReadRepository
    {
        public Subasta? Auction { get; set; }

        public IReadOnlyList<Guid> WinningBidUserIds { get; set; }
            = Array.Empty<Guid>();

        public bool HasAnyBid { get; set; }

        public bool HasUserBid { get; set; }

        public Task<bool> AuctionExistsAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Auction?.Id == subastaId);
        }

        public Task<IReadOnlyList<BidHistoryResult>> GetHistoryAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BidHistoryResult>>(
                Array.Empty<BidHistoryResult>());
        }

        public Task<Subasta?> GetAuctionAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Auction?.Id == subastaId ? Auction : null);
        }

        public Task<IReadOnlyList<Guid>> GetWinningBidUserIdsAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WinningBidUserIds);
        }

        public Task<bool> HasAnyBidAsync(
            Guid subastaId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HasAnyBid);
        }

        public Task<bool> HasUserBidAsync(
            Guid subastaId,
            Guid usuarioId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HasUserBid);
        }
    }
}