namespace SubastaYa.API.Services;

public sealed record AuctionClosingResult(
    Guid AuctionId,
    AuctionClosingOutcome Outcome,
    Guid? SettlementId = null);
