using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Notifications;

public interface IAuctionNotifier
{
    Task NotifyStateChangedAsync(Guid auctionId, EstadoSubasta state, DateTimeOffset endDateUtc, CancellationToken cancellationToken = default);
}
