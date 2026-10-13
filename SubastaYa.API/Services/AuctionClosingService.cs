using Microsoft.AspNetCore.SignalR;
using SubastaYa.API.Hubs;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;
using SubastaYa.Infrastructure.Repositories;

namespace SubastaYa.API.Services;

// Adaptador de compatibilidad para consumidores existentes. La regla de negocio
// reside en CloseAuctionCommandHandler y la persistencia en Infrastructure.
public sealed class AuctionClosingService : IAuctionClosingService
{
    private readonly CloseAuctionCommandHandler _handler;

    public AuctionClosingService(
        ApplicationDbContext dbContext,
        IHubContext<AuctionHub> hubContext,
        TimeProvider timeProvider,
        ILogger<AuctionClosingService> logger)
    {
        _handler = new CloseAuctionCommandHandler(
            new AuctionClosingRepository(dbContext),
            new CompatibilityNotifier(hubContext, logger),
            timeProvider);
    }

    public async Task<AuctionClosingResult> ProcessAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _handler.HandleAsync(
            new CloseAuctionCommand(auctionId), cancellationToken);
        return new AuctionClosingResult(
            result.AuctionId,
            Enum.Parse<AuctionClosingOutcome>(result.Outcome.ToString()),
            result.SettlementId);
    }

    private sealed class CompatibilityNotifier : IAuctionNotifier
    {
        private readonly IHubContext<AuctionHub> _hubContext;
        private readonly ILogger _logger;
        public CompatibilityNotifier(IHubContext<AuctionHub> hubContext, ILogger logger)
        { _hubContext = hubContext; _logger = logger; }

        public async Task NotifyStateChangedAsync(
            Guid auctionId,
            EstadoSubasta state,
            DateTimeOffset endDateUtc,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _hubContext.Clients.Group($"auction-{auctionId}").SendAsync(
                    "AuctionStateChanged",
                    new { SubastaId = auctionId, Estado = state.ToString(), FechaFinUtc = endDateUtc },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "La subasta {AuctionId} fue procesada, pero no pudo notificarse por SignalR.", auctionId);
            }
        }
    }
}
