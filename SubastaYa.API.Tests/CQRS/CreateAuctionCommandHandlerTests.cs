using SubastaYa.API.Tests.TestInfrastructure;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Domain.Models;

namespace SubastaYa.API.Tests.CQRS;

public sealed class CreateAuctionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        10,
        13,
        15,
        0,
        0,
        TimeSpan.Zero);

    public static IEnumerable<object[]> InvalidCommands()
    {
        yield return [Create(title: string.Empty), AuctionCreationRules.TitleRequired];
        yield return [Create(title: "   "), AuctionCreationRules.TitleRequired];
        yield return [Create(description: "\t"), AuctionCreationRules.DescriptionRequired];
        yield return [Create(category: string.Empty), AuctionCreationRules.CategoryRequired];
        yield return [Create(category: "  "), AuctionCreationRules.CategoryRequired];
        yield return [Create(initialPrice: 0), AuctionCreationRules.InitialPriceMustBePositive];
        yield return [Create(initialPrice: -1), AuctionCreationRules.InitialPriceMustBePositive];
        yield return [Create(minimumIncrement: 0), AuctionCreationRules.MinimumIncrementMustBePositive];
        yield return [Create(minimumIncrement: -1), AuctionCreationRules.MinimumIncrementMustBePositive];
        yield return [
            Create(startDateUtc: Now.AddHours(2), endDateUtc: Now.AddHours(2)),
            AuctionCreationRules.EndDateMustBeAfterStartDate];
        yield return [
            Create(startDateUtc: Now.AddHours(-2), endDateUtc: Now),
            AuctionCreationRules.EndDateMustBeFuture];
        yield return [
            Create(startDateUtc: Now.AddHours(-2), endDateUtc: Now.AddMinutes(-1)),
            AuctionCreationRules.EndDateMustBeFuture];
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task HandleAsync_WithInvalidCommand_RejectsWithoutPersisting(
        CreateAuctionCommand command,
        string expectedMessage)
    {
        var repository = new FakeAuctionRepository();
        var handler = new CreateAuctionCommandHandler(
            repository,
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<AuctionValidationException>(
            () => handler.HandleAsync(command));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Null(repository.AddedAuction);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Theory]
    [InlineData(-1, EstadoSubasta.Activa)]
    [InlineData(1, EstadoSubasta.Programada)]
    public async Task HandleAsync_WithValidCommand_NormalizesAndPersistsAuction(
        int startOffsetMinutes,
        EstadoSubasta expectedState)
    {
        var repository = new FakeAuctionRepository();
        var handler = new CreateAuctionCommandHandler(
            repository,
            new FixedTimeProvider(Now));
        var command = Create(
            title: "  Notebook  ",
            description: "  Equipo nuevo  ",
            category: "  Tecnología  ",
            imageUrl: "  https://example.test/notebook.jpg  ",
            startDateUtc: Now.AddMinutes(startOffsetMinutes),
            endDateUtc: Now.AddHours(2));

        var result = await handler.HandleAsync(command);

        var auction = Assert.IsType<Subasta>(repository.AddedAuction);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Equal("Notebook", auction.Titulo);
        Assert.Equal("Equipo nuevo", auction.Descripcion);
        Assert.Equal("Tecnología", auction.Categoria);
        Assert.Equal("https://example.test/notebook.jpg", auction.ImagenUrl);
        Assert.Equal(expectedState, auction.Estado);
        Assert.Equal(command.InitialPrice, auction.PrecioInicial);
        Assert.Equal(command.InitialPrice, auction.PrecioActual);
        Assert.Equal(command.MinimumIncrement, auction.IncrementoMinimo);
        Assert.Equal(expectedState, result.Estado);
    }

    private static CreateAuctionCommand Create(
        string title = "Notebook",
        string description = "Equipo nuevo",
        string? imageUrl = null,
        string category = "Tecnología",
        decimal initialPrice = 100,
        decimal minimumIncrement = 10,
        DateTimeOffset? startDateUtc = null,
        DateTimeOffset? endDateUtc = null) =>
        new(
            Guid.NewGuid(),
            title,
            description,
            imageUrl,
            category,
            initialPrice,
            minimumIncrement,
            startDateUtc ?? Now,
            endDateUtc ?? Now.AddHours(1));

    private sealed class FakeAuctionRepository : IAuctionRepository
    {
        public Subasta? AddedAuction { get; private set; }
        public int SaveChangesCount { get; private set; }

        public void Add(Subasta auction) => AddedAuction = auction;

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }

        public Task<PagedAuctionResult> SearchAsync(string? search, decimal? minPrice, decimal? maxPrice, string? category, EstadoSubasta? state, string? order, int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuctionResult>> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BidActivityResult>> GetBidActivitiesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Subasta?> GetOwnedTrackedAsync(Guid auctionId, Guid sellerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Subasta?> GetTrackedAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasBidsAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasSettlementAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasAuditAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetScheduledIdsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetExpiredIdsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Remove(Subasta auction) => throw new NotSupportedException();
        public void AddAudit(AuditoriaLog audit) => throw new NotSupportedException();
    }
}
