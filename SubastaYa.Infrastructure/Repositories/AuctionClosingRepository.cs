using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SubastaYa.Infrastructure.Repositories;

public sealed class AuctionClosingRepository : IAuctionClosingRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AuctionClosingRepository> _logger;
    private AuctionClosingTransaction? _transaction;

    public AuctionClosingRepository(ApplicationDbContext dbContext)
        : this(dbContext, NullLogger<AuctionClosingRepository>.Instance)
    {
    }

    public AuctionClosingRepository(
        ApplicationDbContext dbContext,
        ILogger<AuctionClosingRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        if (_transaction is not null)
            throw new InvalidOperationException(
                "Ya existe una transacción de cierre activa.");

        var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        _transaction = new AuctionClosingTransaction(
            transaction,
            exception => _logger.LogWarning(
                exception,
                "La operación transaccional de cierre terminó, pero falló la liberación de sus recursos."));
    }
    public Task<Subasta?> GetAuctionAsync(Guid id, CancellationToken ct) => _dbContext.Subastas.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> HasSettlementAsync(Guid id, CancellationToken ct) => _dbContext.LiquidacionesSubasta.AsNoTracking().AnyAsync(x => x.SubastaId == id, ct);
    public async Task<IReadOnlyList<Puja>> GetWinningBidsAsync(Guid id, CancellationToken ct) => await _dbContext.Pujas.Where(x => x.SubastaId == id && x.EsGanadora).Take(2).ToListAsync(ct);
    public Task<bool> HasBidsAsync(Guid id, CancellationToken ct) => _dbContext.Pujas.AsNoTracking().AnyAsync(x => x.SubastaId == id, ct);
    public async Task<IReadOnlyList<decimal>> GetBidAmountsAsync(Guid id, CancellationToken ct) => await _dbContext.Pujas.AsNoTracking().Where(x => x.SubastaId == id).Select(x => x.Monto).ToListAsync(ct);
    public Task<Billetera?> GetWalletAsync(Guid userId, CancellationToken ct) => _dbContext.Billeteras.SingleOrDefaultAsync(x => x.UsuarioId == userId, ct);
    public void AddSettlement(LiquidacionSubasta settlement) => _dbContext.LiquidacionesSubasta.Add(settlement);
    public void AddLedgerTransactions(params TransaccionLedger[] transactions) => _dbContext.TransaccionLedgers.AddRange(transactions);
    public void AddAudit(AuditoriaLog audit) => _dbContext.AuditoriaLogs.Add(audit);
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await _dbContext.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { throw new ClosingPersistenceConcurrencyException(ex); }
        catch (DbUpdateException ex) when (IsIdempotencyConflict(ex)) { throw new ClosingIdempotencyException(ex); }
    }
    public async Task CommitAsync(CancellationToken ct)
    {
        var transaction = _transaction ?? throw new InvalidOperationException(
            "No existe una transacción de cierre activa para confirmar.");

        await transaction.CommitAsync(ct);
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        var transaction = _transaction ?? throw new InvalidOperationException(
            "No existe una transacción de cierre activa para revertir.");

        try
        {
            await transaction.RollbackAsync(ct);
            _dbContext.ChangeTracker.Clear();
        }
        finally
        {
            _transaction = null;
        }
    }
    public async Task<bool> IsAlreadyProcessedAsync(Guid id, CancellationToken ct)
    {
        var state = await _dbContext.Subastas.AsNoTracking().Where(x => x.Id == id).Select(x => (EstadoSubasta?)x.Estado).SingleOrDefaultAsync(ct);
        if (state == EstadoSubasta.Desierta)
            return await _dbContext.AuditoriaLogs.AsNoTracking().AnyAsync(x => x.ClaveIdempotencia == $"auction:deserted:{id}", ct) && !await HasSettlementAsync(id, ct);
        if (state != EstadoSubasta.Finalizada) return false;
        var settlementId = await _dbContext.LiquidacionesSubasta.AsNoTracking().Where(x => x.SubastaId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (!settlementId.HasValue) return false;
        var movements = await _dbContext.TransaccionLedgers.AsNoTracking().Where(x => x.LiquidacionSubastaId == settlementId).Select(x => x.Tipo).ToListAsync(ct);
        var audit = await _dbContext.AuditoriaLogs.AsNoTracking().AnyAsync(x => x.ClaveIdempotencia == $"auction:settled:{id}", ct);
        return movements.Count == 2 && movements.Contains(TipoMovimientoBilletera.Pago) && movements.Contains(TipoMovimientoBilletera.Cobro) && audit;
    }
    private static bool IsIdempotencyConflict(DbUpdateException ex)
    {
        var error = ex.ToString();
        string[] keys = ["UX_LiquidacionesSubasta_SubastaId", "UX_LiquidacionesSubasta_PujaGanadoraId", "UX_TransaccionesLedger_LiquidacionSubastaId_Tipo", "UX_TransaccionesLedger_ClaveIdempotencia", "UX_AuditoriaLogs_ClaveIdempotencia", "LiquidacionesSubasta.SubastaId", "LiquidacionesSubasta.PujaGanadoraId", "TransaccionesLedger.ClaveIdempotencia", "AuditoriaLogs.ClaveIdempotencia"];
        return keys.Any(key => error.Contains(key, StringComparison.OrdinalIgnoreCase));
    }
}
