using Microsoft.EntityFrameworkCore;
using SubastaYa.Aplicacion.Repositories;
using SubastaYa.Dominio.Models;
using SubastaYa.Infraestructura.Data;

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
        await _dbContext.SaveChangesAsync(cancellationToken);
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
}