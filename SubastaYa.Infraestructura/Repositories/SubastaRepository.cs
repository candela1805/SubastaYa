using Microsoft.EntityFrameworkCore;
using SubastaYa.Aplicacion.Contracts.Auctions;
using SubastaYa.Aplicacion.Repositories;
using SubastaYa.Dominio.Models;
using SubastaYa.Infraestructura.Data;
using SubastaYa.Aplicacion.Exceptions;

namespace SubastaYa.Infraestructura.Repositories;

public sealed class SubastaRepository : ISubastaRepository
{
    private readonly ApplicationDbContext _dbContext;

    public SubastaRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Agregar(Subasta subasta)
    {
        _dbContext.Subastas.Add(subasta);
    }

    public async Task GuardarCambiosAsync(
    CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictoConcurrenciaException(
                "Los datos cambiaron mientras se guardaba la operación.",
                ex);
        }
        catch (DbUpdateException ex)
        {
            throw new PersistenciaException(
                "No se pudieron guardar los cambios.",
                ex);
        }
    }

    public async Task<IReadOnlyList<Subasta>> ObtenerPorVendedorAsync(
        Guid vendedorId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Subastas
            .AsNoTracking()
            .Where(subasta => subasta.VendedorId == vendedorId)
            .OrderByDescending(subasta => subasta.FechaInicioUtc)
            .ThenByDescending(subasta => subasta.Id)
            .ToListAsync(cancellationToken);
    }
    public void AgregarAuditoria(AuditoriaLog auditoria)
    {
        _dbContext.AuditoriaLogs.Add(auditoria);
    }

    public Task<Subasta?> ObtenerPorIdAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Subastas.SingleOrDefaultAsync(
            subasta => subasta.Id == subastaId,
            cancellationToken);
    }

    public Task<Subasta?> ObtenerDelVendedorAsync(
        Guid subastaId,
        Guid vendedorId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Subastas.SingleOrDefaultAsync(
            subasta =>
                subasta.Id == subastaId &&
                subasta.VendedorId == vendedorId,
            cancellationToken);
    }

    public Task<bool> TienePujasAsync(
    Guid subastaId,
    CancellationToken cancellationToken = default)
    {
        return _dbContext.Pujas.AnyAsync(
            puja => puja.SubastaId == subastaId,
            cancellationToken);
    }

    public Task<bool> TieneLiquidacionAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.LiquidacionesSubasta.AnyAsync(
            liquidacion => liquidacion.SubastaId == subastaId,
            cancellationToken);
    }

    public async Task PrepararEliminacionAsync(
        Subasta subasta,
        CancellationToken cancellationToken = default)
    {
        var auditorias = await _dbContext.AuditoriaLogs
            .Where(auditoria => auditoria.SubastaId == subasta.Id)
            .ToListAsync(cancellationToken);

        _dbContext.AuditoriaLogs.RemoveRange(auditorias);
        _dbContext.Subastas.Remove(subasta);
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
}