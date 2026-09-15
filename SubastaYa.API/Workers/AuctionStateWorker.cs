using Microsoft.Extensions.Options;

namespace SubastaYa.API.Workers;

public sealed class AuctionStateWorker : BackgroundService
{
    private readonly IAuctionStateCycleProcessor _cycleProcessor;
    private readonly IOptionsMonitor<AuctionClosingWorkerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuctionStateWorker> _logger;

    public AuctionStateWorker(
        IAuctionStateCycleProcessor cycleProcessor,
        IOptionsMonitor<AuctionClosingWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<AuctionStateWorker> logger)
    {
        _cycleProcessor = cycleProcessor;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            if (options.Enabled)
            {
                await RunCycleAsync(options, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.IntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(
        AuctionClosingWorkerOptions options,
        CancellationToken stoppingToken)
    {
        var startedAt = _timeProvider.GetUtcNow();

        _logger.LogInformation(
            "Iniciando ciclo automático de subastas a las {StartedAt}; lote {BatchSize}.",
            startedAt,
            options.BatchSize);

        try
        {
            var result = await _cycleProcessor.ProcessCycleAsync(
                options.BatchSize,
                stoppingToken);

            _logger.LogInformation(
                "Ciclo automático finalizado. Programadas encontradas: {ScheduledFound}; activadas: {Activated}; vencidas encontradas: {ExpiredFound}; finalizadas: {Finalized}; desiertas: {Deserted}; omitidas: {Skipped}; conflictos: {Conflicts}; errores: {Errors}.",
                result.ScheduledAuctionsFound,
                result.AuctionsActivated,
                result.ExpiredAuctionsFound,
                result.AuctionsFinalized,
                result.AuctionsDeserted,
                result.AuctionsSkipped,
                result.ConcurrencyConflicts,
                result.Errors);
        }
        catch (OperationCanceledException)
        when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Falló el ciclo automático de estados de subasta iniciado a las {StartedAt}.",
                startedAt);
        }
    }
}
