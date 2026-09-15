using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

namespace SubastaYa.API.Workers;

public sealed class AuctionStateCycleProcessor : IAuctionStateCycleProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuctionStateCycleProcessor> _logger;

    public AuctionStateCycleProcessor(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AuctionStateCycleProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuctionStateCycleResult> ProcessCycleAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "El tamaño del lote debe ser mayor a cero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        IReadOnlyList<Guid> scheduledAuctionIds;
        IReadOnlyList<Guid> expiredAuctionIds;

        using (var discoveryScope = _scopeFactory.CreateScope())
        {
            var dbContext = discoveryScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            scheduledAuctionIds = await dbContext.Subastas
                .AsNoTracking()
                .Where(auction =>
                    auction.Estado == EstadoSubasta.Programada &&
                    auction.FechaInicioUtc <= now)
                .OrderBy(auction => auction.FechaInicioUtc)
                .ThenBy(auction => auction.Id)
                .Select(auction => auction.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            expiredAuctionIds = await dbContext.Subastas
                .AsNoTracking()
                .Where(auction =>
                    auction.Estado == EstadoSubasta.Activa &&
                    auction.FechaFinUtc <= now)
                .OrderBy(auction => auction.FechaFinUtc)
                .ThenBy(auction => auction.Id)
                .Select(auction => auction.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        var activated = 0;
        var finalized = 0;
        var deserted = 0;
        var skipped = 0;
        var concurrencyConflicts = 0;
        var errors = 0;

        foreach (var auctionId in scheduledAuctionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var resultingState = await ActivateAuctionAsync(
                    auctionId,
                    cancellationToken);

                if (resultingState == EstadoSubasta.Activa)
                {
                    activated += 1;
                }
            }
            catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors += 1;
                _logger.LogError(
                    exception,
                    "No se pudo activar la subasta {AuctionId}.",
                    auctionId);
            }
        }

        foreach (var auctionId in expiredAuctionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await CloseAuctionAsync(
                    auctionId,
                    cancellationToken);

                if (result.Outcome == AuctionClosingOutcome.Finalized)
                {
                    finalized += 1;
                }
                else if (result.Outcome == AuctionClosingOutcome.Deserted)
                {
                    deserted += 1;
                }
                else if (result.Outcome ==
                    AuctionClosingOutcome.ConcurrencyConflict)
                {
                    concurrencyConflicts += 1;
                }
                else
                {
                    skipped += 1;
                }
            }
            catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors += 1;
                _logger.LogError(
                    exception,
                    "No se pudo procesar la subasta vencida {AuctionId}.",
                    auctionId);
            }
        }

        return new AuctionStateCycleResult(
            scheduledAuctionIds.Count,
            activated,
            expiredAuctionIds.Count,
            finalized,
            deserted,
            skipped,
            concurrencyConflicts,
            errors);
    }

    private async Task<EstadoSubasta?> ActivateAuctionAsync(
        Guid auctionId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var auctionService = scope.ServiceProvider
            .GetRequiredService<IAuctionService>();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        await auctionService.ActivarAsync(
            auctionId,
            cancellationToken);

        return await dbContext.Subastas
            .AsNoTracking()
            .Where(auction => auction.Id == auctionId)
            .Select(auction => (EstadoSubasta?)auction.Estado)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<AuctionClosingResult> CloseAuctionAsync(
        Guid auctionId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var closingService = scope.ServiceProvider
            .GetRequiredService<IAuctionClosingService>();

        return await closingService.ProcessAsync(
            auctionId,
            cancellationToken);
    }
}
