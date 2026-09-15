namespace SubastaYa.API.Services;

public enum AuctionClosingOutcome
{
    Skipped = 0,
    NoBids = 1,
    Finalized = 2,
    Deserted = 3,
    AlreadyProcessed = 4,
    ConcurrencyConflict = 5
}
