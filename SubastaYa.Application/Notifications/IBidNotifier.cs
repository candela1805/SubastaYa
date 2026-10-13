using SubastaYa.Application.Features.Bids.DTOs;

namespace SubastaYa.Application.Abstractions.Notifications;

public interface IBidNotifier
{
    Task NotifyBidPlacedAsync(
        Guid subastaId,
        PlaceBidResult result,
        CancellationToken cancellationToken = default);
}