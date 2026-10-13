using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Storage;
using SubastaYa.Application.Exceptions;

namespace SubastaYa.Infrastructure.Repositories;

public sealed class PlaceBidRepository : IPlaceBidRepository, IAsyncDisposable
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _transaction;

    public PlaceBidRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Subasta?> GetAuctionAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return _context.Subastas
            .SingleOrDefaultAsync(
                subasta => subasta.Id == subastaId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Puja>> GetWinningBidsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Pujas
            .Where(puja =>
                puja.SubastaId == subastaId &&
                puja.EsGanadora)
            .Take(2)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasAnyBidAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return _context.Pujas
            .AnyAsync(
                puja => puja.SubastaId == subastaId,
                cancellationToken);
    }

    public Task<Billetera?> GetWalletAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        return _context.Billeteras
            .SingleOrDefaultAsync(
                billetera => billetera.UsuarioId == usuarioId,
                cancellationToken);
    }

    public async Task BeginTransactionAsync(
    CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "Ya existe una transacción activa.");
        }

        _transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BidPersistenceConcurrencyException(
                "La información de la subasta cambió durante la operación.",
                exception);
        }
        catch (DbUpdateException exception)
            when (EsConflictoDePujaGanadora(exception))
        {
            throw new BidPersistenceConcurrencyException(
                "Otra puja tomó el liderazgo mientras se procesaba la solicitud.",
                exception);
        }
    }

    public async Task CommitTransactionAsync(
    CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException(
                "No existe una transacción activa.");
        }

        // Primero confirmamos los cambios.
        await _transaction.CommitAsync(cancellationToken);

        // Desde este punto, el Commit terminó correctamente.
        // Un error al liberar recursos no debe convertirlo
        // en un supuesto fallo de la operación.
        var transaction = _transaction;
        _transaction = null;

        try
        {
            await transaction.DisposeAsync();
        }
        catch
        {
            // El Commit ya fue confirmado.
            // No intentamos revertir una transacción confirmada.
        }
    }

    public async Task RollbackTransactionAsync(
    CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }
    }

    public void AddBid(Puja puja)
    {
        _context.Pujas.Add(puja);
    }

    public void AddLedgerTransaction(TransaccionLedger movimiento)
    {
        _context.TransaccionLedgers.Add(movimiento);
    }

    public void AddAuditLog(AuditoriaLog auditoria)
    {
        _context.AuditoriaLogs.Add(auditoria);
    }

    private static bool EsConflictoDePujaGanadora(
    DbUpdateException exception)
    {
        var error = exception.ToString();

        return error.Contains(
                "UX_Pujas_SubastaId_Ganadora",
                StringComparison.OrdinalIgnoreCase) ||
            error.Contains(
                "UNIQUE constraint failed: Pujas.SubastaId",
                StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }
    }
}
