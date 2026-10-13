using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Features.Bids.Commands.PlaceBid;
using SubastaYa.Application.Features.Bids.DTOs;

namespace SubastaYa.API.Tests.TestInfrastructure;

internal sealed class FakeBidNotifier : IBidNotifier
{
    public int NotificationCount { get; private set; }

    public Task NotifyBidPlacedAsync(
        Guid subastaId,
        PlaceBidResult result,
        CancellationToken cancellationToken = default)
    {
        NotificationCount++;
        return Task.CompletedTask;
    }
}