using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Wallet.Commands.DepositWallet;
using SubastaYa.Application.Features.Wallet.DTOs;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SubastaYa.Infrastructure.Repositories;

public sealed class WalletRepository : IWalletRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<WalletRepository> _logger;
    private WalletTransaction? _transaction;

    public WalletRepository(ApplicationDbContext dbContext)
        : this(dbContext, NullLogger<WalletRepository>.Instance)
    {
    }

    public WalletRepository(
        ApplicationDbContext dbContext,
        ILogger<WalletRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<WalletBalanceResult?> GetBalanceAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _dbContext.Billeteras
            .AsNoTracking()
            .Where(wallet => wallet.UsuarioId == userId)
            .Select(wallet => new WalletBalanceResult(
                wallet.SaldoTotal,
                wallet.SaldoRetenido,
                wallet.SaldoDisponible))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Billetera?> GetTrackedWalletAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _dbContext.Billeteras.FirstOrDefaultAsync(
            wallet => wallet.UsuarioId == userId,
            cancellationToken);

    public Task<TransaccionLedger?> GetLedgerTransactionByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.TransaccionLedgers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                transaction =>
                    transaction.ClaveIdempotencia == idempotencyKey,
                cancellationToken);

    public async Task<IReadOnlyList<WalletTransactionResult>?> GetTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var walletId = await _dbContext.Billeteras
            .AsNoTracking()
            .Where(wallet => wallet.UsuarioId == userId)
            .Select(wallet => (Guid?)wallet.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!walletId.HasValue)
            return null;

        return await _dbContext.TransaccionLedgers
            .AsNoTracking()
            .Where(transaction => transaction.BilleteraId == walletId.Value)
            .OrderByDescending(transaction => transaction.FechaUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Select(transaction => new WalletTransactionResult(
                transaction.Id,
                transaction.Tipo.ToString(),
                transaction.Monto,
                transaction.Descripcion,
                transaction.FechaUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<RetainedFundsResult?> GetRetainedFundsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var retainedTotal = await _dbContext.Billeteras
            .AsNoTracking()
            .Where(wallet => wallet.UsuarioId == userId)
            .Select(wallet => (decimal?)wallet.SaldoRetenido)
            .FirstOrDefaultAsync(cancellationToken);

        if (!retainedTotal.HasValue)
            return null;

        var items = await (
                from bid in _dbContext.Pujas.AsNoTracking()
                join auction in _dbContext.Subastas.AsNoTracking()
                    on bid.SubastaId equals auction.Id
                where bid.UsuarioId == userId &&
                      bid.EsGanadora &&
                      (auction.Estado == EstadoSubasta.Programada ||
                       auction.Estado == EstadoSubasta.Activa)
                orderby bid.FechaUtc descending, bid.Id descending
                select new RetainedFundItemResult(
                    auction.Id,
                    auction.Titulo,
                    bid.Monto,
                    bid.FechaUtc,
                    auction.Estado.ToString()))
            .ToListAsync(cancellationToken);

        var identifiedTotal = items.Sum(item => item.Monto);
        return new RetainedFundsResult(
            retainedTotal.Value,
            identifiedTotal,
            retainedTotal.Value - identifiedTotal,
            items);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
            throw new InvalidOperationException(
                "Ya existe una transacción de depósito activa.");

        var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        _transaction = new WalletTransaction(
            transaction,
            exception => _logger.LogWarning(
                exception,
                "La operación transaccional del depósito terminó, pero falló la liberación de sus recursos."));
    }

    public void AddLedgerTransaction(TransaccionLedger transaction) =>
        _dbContext.TransaccionLedgers.Add(transaction);

    public void AddAuditLog(AuditoriaLog audit) =>
        _dbContext.AuditoriaLogs.Add(audit);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new WalletConcurrencyException(
                "La billetera fue modificada por otra operación. Intente nuevamente.",
                ex);
        }
        catch (DbUpdateException ex) when (IsIdempotencyConflict(ex))
        {
            throw new WalletIdempotencyPersistenceException(ex);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException(
            "No existe una transacción de depósito activa para confirmar.");

        await transaction.CommitAsync(cancellationToken);
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException(
            "No existe una transacción de depósito activa para revertir.");

        try
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }
        finally
        {
            _transaction = null;
        }
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception)
    {
        var error = exception.ToString();
        return error.Contains(
                "UX_TransaccionesLedger_ClaveIdempotencia",
                StringComparison.OrdinalIgnoreCase) ||
            error.Contains(
                "TransaccionesLedger.ClaveIdempotencia",
                StringComparison.OrdinalIgnoreCase) ||
            error.Contains(
                "UX_AuditoriaLogs_ClaveIdempotencia",
                StringComparison.OrdinalIgnoreCase) ||
            error.Contains(
                "AuditoriaLogs.ClaveIdempotencia",
                StringComparison.OrdinalIgnoreCase);
    }
}
