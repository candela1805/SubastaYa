using SubastaYa.Domain.Models;
using SubastaYa.API.Services;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Application.Features.Auctions.Queries;

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
                "El tamaÃƒÂ±o del lote debe ser mayor a cero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        AuctionCycleCandidatesResult candidates;
        using (var discoveryScope = _scopeFactory.CreateScope())
        {
            var query = discoveryScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetAuctionCycleCandidatesQuery, AuctionCycleCandidatesResult>>();
            candidates = await query.HandleAsync(
                new GetAuctionCycleCandidatesQuery(now, batchSize), cancellationToken);
        }
        var scheduledAuctionIds = candidates.ScheduledIds;
        var expiredAuctionIds = candidates.ExpiredIds;

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

                if (result.Outcome == SubastaYa.Application.Features.Auctions.Commands.AuctionClosingOutcome.Finalized)
                {
                    finalized += 1;
                }
                else if (result.Outcome == SubastaYa.Application.Features.Auctions.Commands.AuctionClosingOutcome.Deserted)
                {
                    deserted += 1;
                }
                else if (result.Outcome ==
                    SubastaYa.Application.Features.Auctions.Commands.AuctionClosingOutcome.ConcurrencyConflict)
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
        var command = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<ActivateAuctionCommand, EstadoSubasta?>>();
        return await command.HandleAsync(
            new ActivateAuctionCommand(auctionId), cancellationToken);
    }

    private async Task<SubastaYa.Application.Features.Auctions.Commands.AuctionClosingResult> CloseAuctionAsync(
        Guid auctionId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<CloseAuctionCommand, SubastaYa.Application.Features.Auctions.Commands.AuctionClosingResult>>();
        return await command.HandleAsync(
            new CloseAuctionCommand(auctionId), cancellationToken);
    }
}
