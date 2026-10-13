using Microsoft.EntityFrameworkCore;
using SubastaYa.Aplicacion.Contracts.Auctions;
using SubastaYa.Aplicacion.Repositories;
using SubastaYa.Dominio.Models;
using SubastaYa.Infraestructura.Data;

namespace SubastaYa.Infraestructura.Repositories;

public sealed class CatalogoRepository : ICatalogoRepository
{
    private readonly ApplicationDbContext _dbContext;

    public CatalogoRepository(ApplicationDbContext dbContext)
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
            query = query.Where(subasta =>
                subasta.Titulo.Contains(texto));
        }

        if (precioMin.HasValue)
        {
            query = query.Where(subasta =>
                subasta.PrecioActual >= precioMin.Value);
        }

        if (precioMax.HasValue)
        {
            query = query.Where(subasta =>
                subasta.PrecioActual <= precioMax.Value);
        }

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            var categoriaComparable = categoria.Trim().ToUpper();

            query = query.Where(subasta =>
                subasta.Categoria.Trim().ToUpper() ==
                categoriaComparable);
        }

        if (estado.HasValue)
        {
            query = query.Where(subasta =>
                subasta.Estado == estado.Value);
        }

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
            TotalPages = (int)Math.Ceiling(
                totalItems / (double)pageSize)
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
            .GroupBy(
                categoria => categoria,
                StringComparer.OrdinalIgnoreCase)
            .Select(grupo => grupo.OrderBy(
                categoria => categoria,
                StringComparer.Ordinal).First())
            .OrderBy(
                categoria => categoria,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                categoria => categoria,
                StringComparer.Ordinal)
            .ToList();
    }
}