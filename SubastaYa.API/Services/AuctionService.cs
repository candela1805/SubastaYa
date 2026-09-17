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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuctionService> _logger;

    public AuctionService(
        ApplicationDbContext dbContext,
        IHubContext<AuctionHub> hubContext,
        TimeProvider timeProvider,
        ILogger<AuctionService> logger)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _timeProvider = timeProvider;
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
            var categoriaComparable = categoriaNormalizada.ToUpper();
            query = query.Where(subasta =>
                subasta.Categoria.Trim().ToUpper() == categoriaComparable);
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

    public async Task<IReadOnlyList<string>> ObtenerCategoriasAsync(
        CancellationToken cancellationToken = default)
    {
        var valores = await _dbContext.Subastas
            .AsNoTracking()
            .Where(subasta => subasta.Categoria != null)
            .Select(subasta => subasta.Categoria)
            .ToListAsync(cancellationToken);

        return valores
            .Select(categoria => categoria.Trim())
            .Where(categoria => categoria.Length > 0)
            .GroupBy(categoria => categoria, StringComparer.OrdinalIgnoreCase)
            .Select(grupo => grupo.OrderBy(
                categoria => categoria,
                StringComparer.Ordinal).First())
            .OrderBy(categoria => categoria, StringComparer.OrdinalIgnoreCase)
            .ThenBy(categoria => categoria, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<AuctionResponse> CrearSubastaAsync(
        Guid vendedorId,
        CreateAuctionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ahora = _timeProvider.GetUtcNow();
        var subasta = new Subasta
        {
            VendedorId = vendedorId,
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

    public async Task<IReadOnlyList<AuctionResponse>> ObtenerPublicacionesAsync(
        Guid vendedorId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Subastas
            .AsNoTracking()
            .Where(subasta => subasta.VendedorId == vendedorId)
            .OrderByDescending(subasta => subasta.FechaInicioUtc)
            .ThenByDescending(subasta => subasta.Id)
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
    }

    public async Task<IReadOnlyList<BidActivityResponse>>
        ObtenerActividadesDePujasAsync(
            Guid usuarioId,
            CancellationToken cancellationToken = default)
    {
        var ofertasPropias = _dbContext.Pujas
            .AsNoTracking()
            .Where(puja => puja.UsuarioId == usuarioId)
            .GroupBy(puja => puja.SubastaId)
            .Select(grupo => new
            {
                SubastaId = grupo.Key,
                MiOferta = grupo.Max(puja => puja.Monto)
            });

        return await (
            from oferta in ofertasPropias
            join subasta in _dbContext.Subastas.AsNoTracking()
                on oferta.SubastaId equals subasta.Id
            join liquidacion in _dbContext.LiquidacionesSubasta.AsNoTracking()
                on subasta.Id equals liquidacion.SubastaId into liquidaciones
            from liquidacion in liquidaciones.DefaultIfEmpty()
            orderby subasta.FechaFinUtc descending, subasta.Id
            select new BidActivityResponse
            {
                SubastaId = subasta.Id,
                Titulo = subasta.Titulo,
                Descripcion = subasta.Descripcion ?? string.Empty,
                ImagenUrl = subasta.ImagenUrl,
                Categoria = subasta.Categoria,
                PrecioInicial = subasta.PrecioInicial,
                MiOferta = oferta.MiOferta,
                OfertaActual = subasta.PrecioActual,
                IncrementoMinimo = subasta.IncrementoMinimo,
                FechaInicioUtc = subasta.FechaInicioUtc,
                FechaFinUtc = subasta.FechaFinUtc,
                Estado = subasta.Estado,
                Resultado = subasta.Estado == EstadoSubasta.Programada ||
                    subasta.Estado == EstadoSubasta.Activa
                        ? "EnCurso"
                        : liquidacion != null &&
                            liquidacion.CompradorId == usuarioId
                            ? "Ganada"
                            : "Perdida"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AuctionResponse> ActualizarPublicacionAsync(
        Guid subastaId,
        Guid vendedorId,
        UpdateAuctionRequest request,
        CancellationToken cancellationToken = default)
    {
        var subasta = await ObtenerPublicacionDelVendedorAsync(
            subastaId,
            vendedorId,
            cancellationToken);

        if (subasta.Estado is EstadoSubasta.Finalizada or EstadoSubasta.Desierta)
        {
            throw new AuctionOperationConflictException(
                "Las publicaciones finalizadas o desiertas no pueden editarse.");
        }

        if (await _dbContext.Pujas.AnyAsync(
                puja => puja.SubastaId == subastaId,
                cancellationToken))
        {
            throw new AuctionOperationConflictException(
                "La publicación no puede editarse porque ya recibió pujas.");
        }

        if (request.FechaFinUtc <= _timeProvider.GetUtcNow() ||
            request.FechaFinUtc <= subasta.FechaInicioUtc)
        {
            throw new AuctionOperationConflictException(
                "La fecha de finalización debe ser futura y posterior al inicio.");
        }

        subasta.Titulo = request.Titulo.Trim();
        subasta.Descripcion = request.Descripcion.Trim();
        subasta.Categoria = request.Categoria.Trim();
        subasta.ImagenUrl = string.IsNullOrWhiteSpace(request.ImagenUrl)
            ? null
            : request.ImagenUrl.Trim();
        subasta.FechaFinUtc = request.FechaFinUtc;

        _dbContext.AuditoriaLogs.Add(new AuditoriaLog
        {
            Id = Guid.NewGuid(),
            SubastaId = subasta.Id,
            TipoEvento = "AuctionUpdated",
            Detalle = "La publicación fue actualizada por su vendedor.",
            FechaUtc = _timeProvider.GetUtcNow()
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AuctionOperationConflictException(
                "La publicación cambió durante la edición. Actualizá los datos e intentá nuevamente.");
        }

        return MapearRespuesta(subasta);
    }

    public async Task EliminarPublicacionAsync(
        Guid subastaId,
        Guid vendedorId,
        CancellationToken cancellationToken = default)
    {
        var subasta = await ObtenerPublicacionDelVendedorAsync(
            subastaId,
            vendedorId,
            cancellationToken);

        var tienePujas = await _dbContext.Pujas.AnyAsync(
            puja => puja.SubastaId == subastaId,
            cancellationToken);
        var tieneLiquidacion = await _dbContext.LiquidacionesSubasta.AnyAsync(
            liquidacion => liquidacion.SubastaId == subastaId,
            cancellationToken);

        if (tienePujas)
        {
            throw new AuctionOperationConflictException(
                "Esta publicación no puede eliminarse porque ya recibió una puja.");
        }

        if (tieneLiquidacion)
        {
            throw new AuctionOperationConflictException(
                "Esta publicación no puede eliminarse porque tiene una liquidación asociada.");
        }

        var auditorias = await _dbContext.AuditoriaLogs
            .Where(auditoria => auditoria.SubastaId == subastaId)
            .ToListAsync(cancellationToken);

        _dbContext.AuditoriaLogs.RemoveRange(auditorias);
        _dbContext.Subastas.Remove(subasta);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AuctionOperationConflictException(
                "La publicación cambió durante la eliminación. Actualizá los datos e intentá nuevamente.");
        }
        catch (DbUpdateException)
        {
            throw new AuctionOperationConflictException(
                "La publicación generó información histórica y ya no puede eliminarse.");
        }
    }

    private async Task<Subasta> ObtenerPublicacionDelVendedorAsync(
        Guid subastaId,
        Guid vendedorId,
        CancellationToken cancellationToken)
    {
        var subasta = await _dbContext.Subastas.SingleOrDefaultAsync(
            item => item.Id == subastaId && item.VendedorId == vendedorId,
            cancellationToken);

        if (subasta is null)
        {
            throw new KeyNotFoundException(
                "La publicación no existe o no pertenece al usuario autenticado.");
        }

        return subasta;
    }

    public async Task ActivarAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        var subasta = await _dbContext.Subastas
            .FirstOrDefaultAsync(s => s.Id == subastaId, cancellationToken);

        if (subasta is null)
        {
            throw new KeyNotFoundException("la subasta no existe.");
        }

        var ahora = _timeProvider.GetUtcNow();
        var estadoAnterior = subasta.Estado;
        var nuevoEstado = EstadoSubasta.Activa;

        if (estadoAnterior != EstadoSubasta.Programada ||
            subasta.FechaInicioUtc > ahora)
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
            FechaUtc = ahora
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
