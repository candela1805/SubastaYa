namespace SubastaYa.API.Services;

public interface IAuctionClosingService
{
    Task<AuctionClosingResult> ProcessAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default);
}
