using Microsoft.AspNetCore.SignalR;

namespace SubastaYa.API.Hubs;

public class AuctionHub : Hub
{
    public async Task JoinAuction(Guid subastaId)
    {
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"auction-{subastaId}");
    }

    public async Task LeaveAuction(Guid subastaId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            $"auction-{subastaId}");
    }
}
