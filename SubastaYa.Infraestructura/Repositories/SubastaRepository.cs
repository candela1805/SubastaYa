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
}