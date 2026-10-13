using Microsoft.AspNetCore.SignalR;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.API.Hubs;

namespace SubastaYa.API.Adapters;

public sealed class SignalRBidNotifier : IBidNotifier
{
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<SignalRBidNotifier> _logger;

    public SignalRBidNotifier(
    IHubContext<AuctionHub> hubContext,
    ILogger<SignalRBidNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyBidPlacedAsync(
        Guid subastaId,
        PlaceBidResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients
                .Group($"auction-{subastaId}")
                .SendAsync(
                    "BidPlaced",
                    result,
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "No se pudo notificar la puja {BidId} de la subasta {AuctionId}.",
                result.Id,
                subastaId);
        }
    }
}