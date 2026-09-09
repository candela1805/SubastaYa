using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

namespace SubastaYa.API.Workers;

public sealed class AuctionStateWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuctionStateWorker> _logger;

    public AuctionStateWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AuctionStateWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarSubastaAsync(stoppingToken);
            }

            catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested) 
            {
                break;
            }

            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Error al procesar los estados de las subastas.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(5),
                stoppingToken);
        }
    }

    private async Task ProcesarSubastaAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var auctionService = scope.ServiceProvider
            .GetRequiredService<IAuctionService>();

        var ahora = DateTimeOffset.UtcNow;

        var programadas = await dbContext.Subastas
            .AsNoTracking()
            .Where(s => 
                s.Estado == EstadoSubasta.Programada && 
                s.FechaInicioUtc <= ahora)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var subastaId in programadas)
        {
            await auctionService.CambiarEstadoAsync(
                subastaId,
                EstadoSubasta.Activa,
                cancellationToken);
        }

        var vencidas = await dbContext.Subastas
            .AsNoTracking()
            .Where(s => 
                s.Estado == EstadoSubasta.Activa &&
                s.FechaFinUtc <= ahora)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var subastaId in vencidas)
        {
            var tienePujas = await dbContext.Pujas
                .AsNoTracking()
                .AnyAsync(
                    p => p.SubastaId == subastaId,
                    cancellationToken);

            var nuevoEstado = tienePujas
                ? EstadoSubasta.Finalizada
                : EstadoSubasta.Desierta;

            await auctionService.CambiarEstadoAsync(
                subastaId,
                nuevoEstado,
                cancellationToken); 
        }

    }
}
