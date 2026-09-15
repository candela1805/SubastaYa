using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SubastaYa.API.Data;
using SubastaYa.API.Models;

namespace SubastaYa.API.Tests.TestInfrastructure;

internal sealed class ConcurrentBidSaveInterceptor : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _bothRequestsReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public bool Enabled { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled ||
            eventData.Context is not ApplicationDbContext context ||
            !context.ChangeTracker.Entries<Puja>()
                .Any(entry => entry.State == EntityState.Added))
        {
            return result;
        }

        if (Interlocked.Increment(ref _arrivals) == 2)
        {
            _bothRequestsReady.TrySetResult();
        }

        await _bothRequestsReady.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        return result;
    }
}
