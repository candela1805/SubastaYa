using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SubastaYa.API.Contracts.Auctions;
using SubastaYa.API.Data;
using SubastaYa.API.Hubs;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public sealed class AuctionService : IAuctionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<AuctionService> _logger;

    public AuctionService(
        ApplicationDbContext dbContext,
        IHubContext<AuctionHub> hubContext,
        ILogger<AuctionService> logger)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<PagedAuctionResponse> ObtenerSubastasAsync(
        string? buscar,
        decimal? precioMin,
        decimal? precioMax,
        string? categoria,
        EstadoSubasta? estado,
        string? ordenar,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Subastas.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = buscar.Trim();
            query = query.Where(subasta => subasta.Titulo.Contains(texto));
        }

        if (precioMin.HasValue)
            query = query.Where(subasta => subasta.PrecioActual >= precioMin.Value);

        if (precioMax.HasValue)
            query = query.Where(subasta => subasta.PrecioActual <= precioMax.Value);

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            var categoriaNormalizada = categoria.Trim();
            query = query.Where(subasta => subasta.Categoria == categoriaNormalizada);
        }

        if (estado.HasValue)
            query = query.Where(subasta => subasta.Estado == estado.Value);

        var totalItems = await query.CountAsync(cancellationToken);

        query = ordenar == "precio"
            ? query.OrderByDescending(subasta => subasta.PrecioActual)
                .ThenBy(subasta => subasta.FechaFinUtc)
            : query.OrderBy(subasta => subasta.FechaFinUtc)
                .ThenBy(subasta => subasta.Id);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(subasta => new AuctionResponse
            {
                Id = subasta.Id,
                Titulo = subasta.Titulo,
                Descripcion = subasta.Descripcion ?? string.Empty,
                ImagenUrl = subasta.ImagenUrl,
                Categoria = subasta.Categoria,
                PrecioInicial = subasta.PrecioInicial,
                PrecioActual = subasta.PrecioActual,
                IncrementoMinimo = subasta.IncrementoMinimo,
                FechaInicioUtc = subasta.FechaInicioUtc,
                FechaFinUtc = subasta.FechaFinUtc,
                Estado = subasta.Estado
            })
            .ToListAsync(cancellationToken);

        return new PagedAuctionResponse
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<AuctionResponse> CrearSubastaAsync(
        CreateAuctionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ahora = DateTimeOffset.UtcNow;
        var subasta = new Subasta
        {
            Titulo = request.Titulo.Trim(),
            Descripcion = request.Descripcion.Trim(),
            ImagenUrl = string.IsNullOrWhiteSpace(request.ImagenUrl)
                ? null
                : request.ImagenUrl.Trim(),
            Categoria = request.Categoria.Trim(),
            PrecioInicial = request.PrecioInicial,
            PrecioActual = request.PrecioInicial,
            IncrementoMinimo = request.IncrementoMinimo,
            FechaInicioUtc = request.FechaInicioUtc,
            FechaFinUtc = request.FechaFinUtc,
            Estado = request.FechaInicioUtc > ahora
                ? EstadoSubasta.Programada
                : EstadoSubasta.Activa
        };

        _dbContext.Subastas.Add(subasta);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapearRespuesta(subasta);
    }

    public async Task CambiarEstadoAsync(
        Guid subastaId,
        EstadoSubasta nuevoEstado,
        CancellationToken cancellationToken = default)
    {
        var subasta = await _dbContext.Subastas
            .FirstOrDefaultAsync(s => s.Id == subastaId, cancellationToken);

        if (subasta is null)
        {
            throw new KeyNotFoundException("la subasta no existe.");
        }

        var ahora = DateTimeOffset.UtcNow;
        var estadoAnterior = subasta.Estado;

        if (nuevoEstado == EstadoSubasta.Activa &&
            (estadoAnterior != EstadoSubasta.Programada ||
             subasta.FechaInicioUtc > ahora))
        {
            return;
        }

        if (nuevoEstado is EstadoSubasta.Finalizada or EstadoSubasta.Desierta)
        {
            if (estadoAnterior != EstadoSubasta.Activa ||
                subasta.FechaFinUtc > ahora)
            {
                return;
            }

            var tienePujas = await _dbContext.Pujas
                .AsNoTracking()
                .AnyAsync(
                    puja => puja.SubastaId == subastaId,
                    cancellationToken);

            nuevoEstado = tienePujas
                ? EstadoSubasta.Finalizada
                : EstadoSubasta.Desierta;
        }

        if (estadoAnterior == nuevoEstado)
        {
            return;
        }

        subasta.Estado = nuevoEstado;

        _dbContext.AuditoriaLogs.Add(new AuditoriaLog
        {
            Id = Guid.NewGuid(),
            SubastaId = subastaId,
            TipoEvento = "AuctionStateChanged",
            Detalle = $"Estado cambiado de {estadoAnterior} a {nuevoEstado}",
            FechaUtc = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _hubContext.Clients
                .Group($"auction-{subastaId}")
                .SendAsync(
                    "AuctionStateChanged",
                    new
                    {
                        SubastaId = subastaId,
                        Estado = nuevoEstado.ToString(),
                        subasta.FechaFinUtc
                    },
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "El estado de la subasta {SubastaId} cambió a {Estado}, pero no pudo notificarse por SignalR.",
                subastaId,
                nuevoEstado);
        }
    }

    private static AuctionResponse MapearRespuesta(Subasta subasta)
    {
        return new AuctionResponse
        {
            Id = subasta.Id,
            Titulo = subasta.Titulo,
            Descripcion = subasta.Descripcion ?? string.Empty,
            ImagenUrl = subasta.ImagenUrl,
            Categoria = subasta.Categoria,
            PrecioInicial = subasta.PrecioInicial,
            PrecioActual = subasta.PrecioActual,
            IncrementoMinimo = subasta.IncrementoMinimo,
            FechaInicioUtc = subasta.FechaInicioUtc,
            FechaFinUtc = subasta.FechaFinUtc,
            Estado = subasta.Estado
        };
    }
}
