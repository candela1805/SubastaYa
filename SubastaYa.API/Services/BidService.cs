using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Data;
using SubastaYa.API.Hubs;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public sealed class BidService : IBidService
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;

    private readonly ApplicationDbContext _context;
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<BidService> _logger;
    private readonly TimeProvider _timeProvider;

    public BidService(
        ApplicationDbContext context,
        IHubContext<AuctionHub> hubContext,
        ILogger<BidService> logger,
        TimeProvider timeProvider)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
        _timeProvider = timeProvider;
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
            .Select(puja => new
            {
                puja.Id,
                puja.Monto,
                puja.UsuarioId,
                puja.FechaUtc
            })
            .ToListAsync(cancellationToken);

        return pujas
            .OrderBy(puja => puja.FechaUtc)
            .ThenBy(puja => puja.Id)
            .Select(puja => new BidHistoryResponse
            {
                Id = puja.Id,
                Monto = puja.Monto,
                Pseudonimo = puja.UsuarioId.ToString()[..8],
                FechaUtc = puja.FechaUtc
            })
            .ToList();
    }

    public async Task<BidRoomStateResponse> GetRoomStateAsync(
        Guid subastaId,
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        var subasta = await _context.Subastas
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == subastaId,
                cancellationToken);

        if (subasta is null)
        {
            throw new BidNotFoundException(
                "AUCTION_NOT_FOUND",
                "La subasta no existe.");
        }

        var pujasGanadoras = await _context.Pujas
            .AsNoTracking()
            .Where(puja => puja.SubastaId == subastaId && puja.EsGanadora)
            .Select(puja => new
            {
                puja.UsuarioId
            })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (pujasGanadoras.Count > 1)
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "La subasta tiene más de una puja ganadora registrada.");
        }

        var pujaGanadora = pujasGanadoras.SingleOrDefault();

        if (pujaGanadora is null && await _context.Pujas
                .AsNoTracking()
                .AnyAsync(
                    puja => puja.SubastaId == subastaId,
                    cancellationToken))
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "La subasta tiene pujas, pero ninguna está marcada como ganadora.");
        }

        var pujaMinimaSiguiente = CalcularPujaMinima(subasta);

        var usuarioParticipo = pujaGanadora is not null &&
            (pujaGanadora.UsuarioId == usuarioId ||
             await _context.Pujas
                 .AsNoTracking()
                 .AnyAsync(
                     puja => puja.SubastaId == subastaId &&
                         puja.UsuarioId == usuarioId,
                     cancellationToken));

        var estadoPostor = !usuarioParticipo
            ? "SinPuja"
            : pujaGanadora!.UsuarioId == usuarioId
                ? "Liderando"
                : "Superado";

        return new BidRoomStateResponse
        {
            SubastaId = subasta.Id,
            PrecioActual = subasta.PrecioActual,
            IncrementoMinimo = subasta.IncrementoMinimo,
            PujaMinimaSiguiente = pujaMinimaSiguiente,
            FechaFinUtc = subasta.FechaFinUtc,
            EstadoSubasta = subasta.Estado,
            EstadoPostor = estadoPostor
        };
    }

    public async Task<BidResponse> PlaceBidAsync(
        Guid subastaId,
        Guid usuarioId,
        PlaceBidRequest request,
        CancellationToken cancellationToken = default)
    {
        var monto = ValidarMonto(request.Monto);
        var ahora = _timeProvider.GetUtcNow();
        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        BidResponse response;

        try
        {
            var subasta = await _context.Subastas
                .SingleOrDefaultAsync(
                    item => item.Id == subastaId,
                    cancellationToken);

            if (subasta is null)
            {
                throw new BidNotFoundException(
                    "AUCTION_NOT_FOUND",
                    "La subasta no existe.");
            }

            if (subasta.VendedorId == usuarioId)
            {
                throw new BidForbiddenException(
                    "OWN_AUCTION_BID",
                    "No podés pujar en tu propia subasta.");
            }

            if (subasta.Estado != EstadoSubasta.Activa)
            {
                throw new BidStateConflictException(
                    "AUCTION_NOT_ACTIVE",
                    "La subasta no está activa.");
            }

            if (ahora < subasta.FechaInicioUtc)
            {
                throw new BidStateConflictException(
                    "AUCTION_NOT_STARTED",
                    "La subasta todavía no comenzó.");
            }

            if (ahora >= subasta.FechaFinUtc)
            {
                throw new BidStateConflictException(
                    "AUCTION_ENDED",
                    "La subasta ya finalizó.");
            }

            var montoMinimo = CalcularPujaMinima(subasta);

            if (monto < montoMinimo)
            {
                throw new BidValidationException(
                    "BID_BELOW_MINIMUM",
                    $"La puja mínima es de {montoMinimo:F2}.");
            }

            var pujasGanadoras = await _context.Pujas
                .Where(puja =>
                    puja.SubastaId == subastaId && puja.EsGanadora)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (pujasGanadoras.Count > 1)
            {
                throw new BidStateConflictException(
                    "BID_STATE_INCONSISTENT",
                    "La subasta tiene más de una puja ganadora registrada.");
            }

            var pujaAnterior = pujasGanadoras.SingleOrDefault();

            if (pujaAnterior is null && await _context.Pujas
                    .AnyAsync(
                        puja => puja.SubastaId == subastaId,
                        cancellationToken))
            {
                throw new BidStateConflictException(
                    "BID_STATE_INCONSISTENT",
                    "La subasta tiene pujas, pero ninguna está marcada como ganadora.");
            }

            if (pujaAnterior is not null &&
                pujaAnterior.Monto != subasta.PrecioActual)
            {
                throw new BidStateConflictException(
                    "BID_STATE_INCONSISTENT",
                    "El estado actual de la subasta es inconsistente. Actualizá los datos e intentá nuevamente.");
            }

            var billetera = await _context.Billeteras
                .SingleOrDefaultAsync(
                    item => item.UsuarioId == usuarioId,
                    cancellationToken);

            if (billetera is null)
            {
                throw new BidInsufficientFundsException(
                    "WALLET_NOT_FOUND",
                    "El usuario no tiene una billetera disponible.");
            }

            var mismoLider = pujaAnterior is not null &&
                pujaAnterior.UsuarioId == usuarioId;
            var montoARequerir = mismoLider
                ? monto - pujaAnterior!.Monto
                : monto;

            if (mismoLider &&
                billetera.SaldoRetenido < pujaAnterior!.Monto)
            {
                throw new BidStateConflictException(
                    "ESCROW_BALANCE_INCONSISTENT",
                    "El saldo retenido del postor actual es inconsistente.");
            }

            if (billetera.SaldoDisponible < montoARequerir)
            {
                throw new BidInsufficientFundsException(
                    "INSUFFICIENT_BALANCE",
                    "Saldo disponible insuficiente para realizar la puja.");
            }

            var nuevaPujaId = Guid.NewGuid();

            if (pujaAnterior is not null && pujaAnterior.UsuarioId != usuarioId)
            {
                var billeteraAnterior = await _context.Billeteras
                    .SingleOrDefaultAsync(
                        item => item.UsuarioId == pujaAnterior.UsuarioId,
                        cancellationToken);

                if (billeteraAnterior is null)
                {
                    throw new BidStateConflictException(
                        "PREVIOUS_BIDDER_WALLET_NOT_FOUND",
                        "El ganador anterior no tiene billetera.");
                }

                if (billeteraAnterior.SaldoRetenido < pujaAnterior.Monto)
                {
                    throw new BidStateConflictException(
                        "ESCROW_BALANCE_INCONSISTENT",
                        "El saldo retenido del ganador anterior es inconsistente.");
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
                        $"Liberación por puja {nuevaPujaId} que superó la oferta en la subasta {subastaId}.",
                    FechaUtc = ahora
                });
            }

            if (pujaAnterior is not null)
            {
                pujaAnterior.EsGanadora = false;
            }

            billetera.SaldoRetenido += montoARequerir;
            billetera.SaldoDisponible -= montoARequerir;

            _context.TransaccionLedgers.Add(new TransaccionLedger
            {
                Id = Guid.NewGuid(),
                BilleteraId = billetera.Id,
                Tipo = TipoMovimientoBilletera.Retencion,
                Monto = montoARequerir,
                Descripcion =
                    $"Retención por puja {nuevaPujaId} en la subasta {subastaId}.",
                FechaUtc = ahora
            });

            var nuevaPuja = new Puja
            {
                Id = nuevaPujaId,
                SubastaId = subastaId,
                UsuarioId = usuarioId,
                Monto = monto,
                EsGanadora = true,
                FechaUtc = ahora
            };

            _context.Pujas.Add(nuevaPuja);
            subasta.PrecioActual = monto;

            var tiempoRestante = subasta.FechaFinUtc - ahora;
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
                        $"La subasta fue extendida por 2 minutos tras la puja {nuevaPujaId} de {monto:F2} en los últimos 60 segundos.",
                    FechaUtc = ahora
                });
            }

            _context.AuditoriaLogs.Add(new AuditoriaLog
            {
                Id = Guid.NewGuid(),
                SubastaId = subastaId,
                TipoEvento = "BidPlaced",
                Detalle =
                    $"Puja {nuevaPujaId} confirmada por {monto:F2} para el usuario {usuarioId}.",
                FechaUtc = ahora
            });

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
                SubastaExtendida = subastaExtendida,
                Estado = "Liderando",
                IncrementoMinimo = subasta.IncrementoMinimo,
                PujaMinimaSiguiente =
                    subasta.PrecioActual + subasta.IncrementoMinimo
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);

            throw new BidConcurrencyException(
                "La subasta cambió mientras se procesaba la puja. Actualizá los datos e intentá nuevamente.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await NotificarPujaAsync(subastaId, response);

        return response;
    }

    private static decimal ValidarMonto(decimal? montoSolicitado)
    {
        if (!montoSolicitado.HasValue || montoSolicitado.Value <= 0)
        {
            throw new BidValidationException(
                "INVALID_BID_AMOUNT",
                "El monto de la puja debe ser mayor a 0.");
        }

        var monto = montoSolicitado.Value;
        var escala = (decimal.GetBits(monto)[3] >> 16) & 0xFF;

        if (escala > 2)
        {
            throw new BidValidationException(
                "INVALID_BID_SCALE",
                "El monto de la puja puede tener como máximo 2 decimales.");
        }

        if (monto > MaxMoneyAmount)
        {
            throw new BidValidationException(
                "BID_AMOUNT_LIMIT_EXCEEDED",
                "El monto de la puja supera el límite monetario permitido.");
        }

        return monto;
    }

    private static decimal CalcularPujaMinima(Subasta subasta)
    {
        if (subasta.PrecioActual < 0 ||
            subasta.IncrementoMinimo <= 0 ||
            subasta.PrecioActual >
                MaxMoneyAmount - subasta.IncrementoMinimo)
        {
            throw new BidStateConflictException(
                "AUCTION_BID_CONFIGURATION_INVALID",
                "La configuración monetaria de la subasta no permite nuevas pujas.");
        }

        return subasta.PrecioActual + subasta.IncrementoMinimo;
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
