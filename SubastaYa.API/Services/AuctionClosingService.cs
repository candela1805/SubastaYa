using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Auctions.Commands;
using ClosingResult =
    SubastaYa.Application.Features.Auctions.Commands.AuctionClosingResult;

namespace SubastaYa.API.Services;

// Adaptador para consumidores que todavía usan IAuctionClosingService.
public sealed class AuctionClosingService : IAuctionClosingService
{
    private readonly ICommandHandler<CloseAuctionCommand, ClosingResult> _handler;

    public AuctionClosingService(
        ICommandHandler<CloseAuctionCommand, ClosingResult> handler)
    {
        _handler = handler;
    }

    public async Task<AuctionClosingResult> ProcessAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _handler.HandleAsync(
            new CloseAuctionCommand(auctionId),
            cancellationToken);

        return new AuctionClosingResult(
            result.AuctionId,
            Enum.Parse<AuctionClosingOutcome>(result.Outcome.ToString()),
            result.SettlementId);
    }
}