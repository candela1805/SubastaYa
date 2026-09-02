using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Auctions;
using SubastaYa.API.Data;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public sealed class AuctionService : IAuctionService
{
    private readonly ApplicationDbContext _dbContext;

    public AuctionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
