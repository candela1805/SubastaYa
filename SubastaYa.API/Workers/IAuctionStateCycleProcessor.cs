namespace SubastaYa.API.Workers;

public interface IAuctionStateCycleProcessor
{
    Task<AuctionStateCycleResult> ProcessCycleAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}
