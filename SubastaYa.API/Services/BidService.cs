using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Data;
using SubastaYa.API.Hubs;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public sealed class BidService : IBidService
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly DbContextOptions<ApplicationDbContext> _dbContextOptions;
    private readonly ILogger<BidService> _logger;

    public BidService(
        ApplicationDbContext context,
        IHubContext<AuctionHub> hubContext,
        DbContextOptions<ApplicationDbContext> dbContextOptions,
        ILogger<BidService> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _dbContextOptions = dbContextOptions;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BidHistoryResponse>> GetBidHistoryAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        var existeSubasta = await _context.Subastas
            .AsNoTracking()
            .AnyAsync(subasta => subasta.Id == subastaId, cancellationToken);

        if (!existeSubasta)
        {
            throw new KeyNotFoundException("La subasta no existe.");
        }

        var pujas = await _context.Pujas
            .AsNoTracking()
            .Where(puja => puja.SubastaId == subastaId)
            .OrderBy(puja => puja.FechaUtc)
            .Select(puja => new
            {
                puja.Id,
                puja.Monto,
                puja.UsuarioId,
                puja.FechaUtc
            })
            .ToListAsync(cancellationToken);

        return pujas
            .Select(puja => new BidHistoryResponse
            {
                Id = puja.Id,
                Monto = puja.Monto,
                Pseudonimo = puja.UsuarioId.ToString()[..8],
                FechaUtc = puja.FechaUtc
            })
            .ToList();
    }

    public async Task<BidResponse> PlaceBidAsync(
        Guid subastaId,
        Guid usuarioId,
        PlaceBidRequest request,
        CancellationToken cancellationToken = default)
    {
        var subasta = await _context.Subastas
            .SingleOrDefaultAsync(
                item => item.Id == subastaId,
                cancellationToken);

        if (subasta is null)
        {
            throw new KeyNotFoundException("La subasta no existe.");
        }

        var ahora = DateTimeOffset.UtcNow;

        if (subasta.Estado != EstadoSubasta.Activa)
        {
            throw new InvalidOperationException("La subasta no está activa.");
        }

        if (ahora < subasta.FechaInicioUtc)
        {
            throw new InvalidOperationException("La subasta todavía no comenzó.");
        }

        if (ahora >= subasta.FechaFinUtc)
        {
            throw new InvalidOperationException("La subasta ya finalizó.");
        }

        var montoMinimo = subasta.PrecioActual + subasta.IncrementoMinimo;

        if (request.Monto < montoMinimo)
        {
            throw new InvalidOperationException(
                $"La puja mínima es de {montoMinimo:F2}.");
        }

        var billetera = await _context.Billeteras
            .SingleOrDefaultAsync(
                item => item.UsuarioId == usuarioId,
                cancellationToken);

        if (billetera is null)
        {
            throw new InvalidOperationException("El usuario no tiene billetera.");
        }

        var pujaAnterior = await _context.Pujas
            .Where(puja => puja.SubastaId == subastaId)
            .OrderByDescending(puja => puja.Monto)
            .ThenByDescending(puja => puja.FechaUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var montoARequerir = pujaAnterior is not null &&
            pujaAnterior.UsuarioId == usuarioId
                ? request.Monto - pujaAnterior.Monto
                : request.Monto;

        if (billetera.SaldoDisponible < montoARequerir)
        {
            throw new InvalidOperationException(
                "Saldo disponible insuficiente para realizar la puja.");
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        BidResponse response;

        try
        {
            if (pujaAnterior is not null && pujaAnterior.UsuarioId != usuarioId)
            {
                var billeteraAnterior = await _context.Billeteras
                    .SingleOrDefaultAsync(
                        item => item.UsuarioId == pujaAnterior.UsuarioId,
                        cancellationToken);

                if (billeteraAnterior is null)
                {
                    throw new InvalidOperationException(
                        "El ganador anterior no tiene billetera.");
                }

                billeteraAnterior.SaldoRetenido -= pujaAnterior.Monto;
                billeteraAnterior.SaldoDisponible += pujaAnterior.Monto;

                _context.TransaccionLedgers.Add(new TransaccionLedger
                {
                    Id = Guid.NewGuid(),
                    BilleteraId = billeteraAnterior.Id,
                    Tipo = TipoMovimientoBilletera.Liberacion,
                    Monto = pujaAnterior.Monto,
                    Descripcion =
                        $"Liberación por haber sido superado en la subasta {subastaId}.",
                    FechaUtc = DateTimeOffset.UtcNow
                });
            }

            var montoARetener = pujaAnterior is not null &&
                pujaAnterior.UsuarioId == usuarioId
                    ? montoARequerir
                    : request.Monto;

            billetera.SaldoRetenido += montoARetener;
            billetera.SaldoDisponible -= montoARetener;

            _context.TransaccionLedgers.Add(new TransaccionLedger
            {
                Id = Guid.NewGuid(),
                BilleteraId = billetera.Id,
                Tipo = TipoMovimientoBilletera.Retencion,
                Monto = montoARetener,
                Descripcion = $"Retención por puja en la subasta {subastaId}.",
                FechaUtc = DateTimeOffset.UtcNow
            });

            var fechaPuja = DateTimeOffset.UtcNow;

            if (fechaPuja >= subasta.FechaFinUtc)
            {
                throw new InvalidOperationException("La subasta ya finalizó.");
            }

            var nuevaPuja = new Puja
            {
                Id = Guid.NewGuid(),
                SubastaId = subastaId,
                UsuarioId = usuarioId,
                Monto = request.Monto,
                FechaUtc = fechaPuja
            };

            _context.Pujas.Add(nuevaPuja);
            subasta.PrecioActual = request.Monto;

            var tiempoRestante = subasta.FechaFinUtc - fechaPuja;
            var subastaExtendida = false;

            if (tiempoRestante <= TimeSpan.FromSeconds(60))
            {
                subasta.FechaFinUtc = subasta.FechaFinUtc.AddMinutes(2);
                subastaExtendida = true;

                _context.AuditoriaLogs.Add(new AuditoriaLog
                {
                    Id = Guid.NewGuid(),
                    SubastaId = subastaId,
                    TipoEvento = "AuctionExtended",
                    Detalle =
                        $"La subasta fue extendida por 2 minutos tras una puja de {request.Monto:F2} en los últimos 60 segundos.",
                    FechaUtc = fechaPuja
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            response = new BidResponse
            {
                Id = nuevaPuja.Id,
                SubastaId = subastaId,
                Monto = nuevaPuja.Monto,
                Pseudonimo = nuevaPuja.UsuarioId.ToString()[..8],
                FechaUtc = nuevaPuja.FechaUtc,
                PrecioActual = subasta.PrecioActual,
                FechaFinUtc = subasta.FechaFinUtc,
                SubastaExtendida = subastaExtendida
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RegistrarRechazoPorConcurrenciaAsync(subastaId, request.Monto);

            throw new BidConcurrencyException(
                "La subasta fue modificada por otra operación. Actualizá los datos e intentá nuevamente.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await NotificarPujaAsync(subastaId, response);

        return response;
    }

    private async Task RegistrarRechazoPorConcurrenciaAsync(
        Guid subastaId,
        decimal monto)
    {
        try
        {
            await using var auditContext =
                new ApplicationDbContext(_dbContextOptions);

            auditContext.AuditoriaLogs.Add(new AuditoriaLog
            {
                Id = Guid.NewGuid(),
                SubastaId = subastaId,
                TipoEvento = "BidConcurrencyRejected",
                Detalle =
                    $"Puja de {monto:F2} rechazada por un conflicto de concurrencia.",
                FechaUtc = DateTimeOffset.UtcNow
            });

            await auditContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo registrar la auditoría del conflicto de la subasta {SubastaId}.",
                subastaId);
        }
    }

    private async Task NotificarPujaAsync(
        Guid subastaId,
        BidResponse response)
    {
        try
        {
            await _hubContext.Clients
                .Group($"auction-{subastaId}")
                .SendAsync("BidPlaced", response, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "La puja {BidId} fue confirmada, pero no pudo notificarse por SignalR.",
                response.Id);
        }
    }
}
