using Microsoft.AspNetCore.SignalR;
using SubastaYa.API.Hubs;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Domain.Models;

namespace SubastaYa.API.Adapters;

public sealed class SignalRAuctionNotifier : IAuctionNotifier
{
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<SignalRAuctionNotifier> _logger;
    public SignalRAuctionNotifier(IHubContext<AuctionHub> hubContext, ILogger<SignalRAuctionNotifier> logger) { _hubContext = hubContext; _logger = logger; }
    public async Task NotifyStateChangedAsync(Guid auctionId, EstadoSubasta state, DateTimeOffset endDateUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"auction-{auctionId}").SendAsync("AuctionStateChanged", new { SubastaId = auctionId, Estado = state.ToString(), FechaFinUtc = endDateUtc }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "El estado de la subasta {AuctionId} cambió a {State}, pero no pudo notificarse por SignalR.", auctionId, state);
        }
    }
}
