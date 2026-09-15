namespace SubastaYa.API.Workers;

public sealed record AuctionStateCycleResult(
    int ScheduledAuctionsFound,
    int AuctionsActivated,
    int ExpiredAuctionsFound,
    int AuctionsFinalized,
    int AuctionsDeserted,
    int Errors);
