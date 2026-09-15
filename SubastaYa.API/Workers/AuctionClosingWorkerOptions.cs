namespace SubastaYa.API.Workers;

public sealed class AuctionClosingWorkerOptions
{
    public const string SectionName = "AuctionClosingWorker";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 50;
}
