using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Wallet.Commands.DepositWallet;
using SubastaYa.Application.Features.Wallet.DTOs;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Repositories;
using SubastaYa.Infrastructure.Data;

namespace SubastaYa.API.Tests.CQRS;

public sealed class WalletFinancialSafetyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid WalletId = Guid.NewGuid();

    [Fact]
    public async Task ConcurrencyConflict_WhenRollbackFails_ReportsUnknownState()
    {
        var concurrencyFailure = new WalletConcurrencyException(
            "conflict",
            new InvalidOperationException("stale wallet"));
        var rollbackFailure = new InvalidOperationException("rollback failed");
        var repository = ValidRepository();
        repository.SaveFailure = concurrencyFailure;
        repository.RollbackFailure = rollbackFailure;

        var exception = await Assert.ThrowsAsync<
            WalletTransactionStateUnknownException>(
            () => CreateHandler(repository).HandleAsync(
                new DepositWalletCommand(UserId, 25m)));

        Assert.Same(concurrencyFailure, exception.OperationException);
        Assert.Same(rollbackFailure, exception.RollbackException);
        Assert.Equal(1, repository.RollbackCount);
    }

    [Fact]
    public async Task ConcurrentDuplicate_AfterConfirmedRollback_ReturnsCommittedBalance()
    {
        var repository = ValidRepository();
        repository.SaveFailure = new WalletConcurrencyException("conflict");
        repository.TransactionAfterRollback = new TransaccionLedger
        {
            BilleteraId = WalletId,
            Tipo = TipoMovimientoBilletera.Deposito,
            Monto = 25m,
            ClaveIdempotencia = $"wallet:deposit:{UserId:N}:retry-001"
        };

        var result = await CreateHandler(repository).HandleAsync(
            new DepositWalletCommand(UserId, 25m, "retry-001"));

        Assert.NotNull(result);
        Assert.Equal(1_025m, result.Total);
        Assert.Equal(1, repository.RollbackCount);
    }

    [Fact]
    public async Task CommitConfirmed_WhenDisposeFails_DoesNotReportFailure()
    {
        var disposalFailure = new InvalidOperationException("dispose failed");
        var transaction = new FakeDbContextTransaction
        {
            DisposeFailure = disposalFailure
        };
        Exception? reportedFailure = null;
        var lifetime = new WalletTransaction(
            transaction,
            exception => reportedFailure = exception);

        await lifetime.CommitAsync(CancellationToken.None);

        Assert.Equal(1, transaction.CommitCount);
        Assert.Equal(1, transaction.DisposeCount);
        Assert.Same(disposalFailure, reportedFailure);
    }

    [Fact]
    public async Task RollbackFailure_IsNotHiddenAndStillDisposesTransaction()
    {
        var rollbackFailure = new InvalidOperationException("rollback failed");
        var transaction = new FakeDbContextTransaction
        {
            RollbackFailure = rollbackFailure
        };
        var lifetime = new WalletTransaction(transaction, _ => { });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifetime.RollbackAsync(CancellationToken.None));

        Assert.Same(rollbackFailure, exception);
        Assert.Equal(1, transaction.RollbackCount);
        Assert.Equal(1, transaction.DisposeCount);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(10.999)]
    public async Task HandlerRejectsMoreThanTwoDecimalsBeforeOpeningTransaction(
        decimal amount)
    {
        var repository = ValidRepository();

        await Assert.ThrowsAsync<WalletValidationException>(
            () => CreateHandler(repository).HandleAsync(
                new DepositWalletCommand(UserId, amount)));

        Assert.Equal(0, repository.BeginCount);
    }

    [Fact]
    public async Task AuditInsertFailure_RollsBackBalanceAndLedgerAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new Usuario
            {
                Id = UserId,
                Nombre = "Wallet test",
                Email = "wallet-atomicity@tests.local",
                PasswordHash = "TEST_ONLY",
                FechaRegistro = DateTimeOffset.UnixEpoch
            };
            setup.Usuarios.Add(user);
            setup.Billeteras.Add(new Billetera
            {
                Id = WalletId,
                UsuarioId = UserId,
                SaldoTotal = 1_000m,
                SaldoDisponible = 1_000m,
                SaldoRetenido = 0m
            });
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER TR_FailWalletDepositAudit
                BEFORE INSERT ON AuditoriaLogs
                WHEN NEW.TipoEvento = 'WalletDeposit'
                BEGIN
                    SELECT RAISE(ABORT, 'forced wallet audit failure');
                END;
                """);
        }

        await using (var actContext = new ApplicationDbContext(options))
        {
            var handler = new DepositWalletCommandHandler(
                new WalletRepository(actContext),
                TimeProvider.System);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => handler.HandleAsync(
                    new DepositWalletCommand(UserId, 25m, "atomic-001")));
        }

        await using var verification = new ApplicationDbContext(options);
        var wallet = await verification.Billeteras.AsNoTracking().SingleAsync();
        Assert.Equal(1_000m, wallet.SaldoTotal);
        Assert.Equal(1_000m, wallet.SaldoDisponible);
        Assert.Empty(await verification.TransaccionLedgers.ToListAsync());
        Assert.Empty(await verification.AuditoriaLogs.ToListAsync());
    }

    [Fact]
    public async Task SqliteRejectsStaleWalletVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new Usuario
            {
                Id = UserId,
                Nombre = "Wallet concurrency",
                Email = "wallet-concurrency@tests.local",
                PasswordHash = "TEST_ONLY",
                FechaRegistro = DateTimeOffset.UnixEpoch
            };
            setup.Usuarios.Add(user);
            setup.Billeteras.Add(new Billetera
            {
                Id = WalletId,
                UsuarioId = UserId,
                SaldoTotal = 1_000m,
                SaldoDisponible = 1_000m,
                SaldoRetenido = 0m
            });
            await setup.SaveChangesAsync();
        }

        await using var first = new ApplicationDbContext(options);
        await using var stale = new ApplicationDbContext(options);
        var firstWallet = await first.Billeteras.SingleAsync();
        var staleWallet = await stale.Billeteras.SingleAsync();
        firstWallet.SaldoTotal += 10m;
        firstWallet.SaldoDisponible += 10m;
        await first.SaveChangesAsync();
        staleWallet.SaldoTotal += 20m;
        staleWallet.SaldoDisponible += 20m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => stale.SaveChangesAsync());
    }

    private static DepositWalletCommandHandler CreateHandler(
        IWalletRepository repository) =>
        new(repository, TimeProvider.System);

    private static FakeWalletRepository ValidRepository() => new()
    {
        Wallet = new Billetera
        {
            Id = WalletId,
            UsuarioId = UserId,
            SaldoTotal = 1_000m,
            SaldoDisponible = 1_000m,
            SaldoRetenido = 0m
        }
    };

    private sealed class FakeWalletRepository : IWalletRepository
    {
        private bool _rolledBack;
        public Billetera? Wallet { get; init; }
        public TransaccionLedger? TransactionAfterRollback { get; set; }
        public Exception? SaveFailure { get; set; }
        public Exception? RollbackFailure { get; set; }
        public int BeginCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task BeginTransactionAsync(CancellationToken cancellationToken)
        {
            BeginCount++;
            return Task.CompletedTask;
        }

        public Task<Billetera?> GetTrackedWalletAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Wallet);
        public Task<TransaccionLedger?> GetLedgerTransactionByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(_rolledBack ? TransactionAfterRollback : null);
        public Task<WalletBalanceResult?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void AddLedgerTransaction(TransaccionLedger transaction) { }
        public void AddAuditLog(AuditoriaLog audit) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            RollbackCount++;
            _rolledBack = true;
            return RollbackFailure is null ? Task.CompletedTask : Task.FromException(RollbackFailure);
        }

        public Task<IReadOnlyList<WalletTransactionResult>?> GetTransactionsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RetainedFundsResult?> GetRetainedFundsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDbContextTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool SupportsSavepoints => false;
        public Exception? RollbackFailure { get; init; }
        public Exception? DisposeFailure { get; init; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void Commit() => CommitCount++;
        public Task CommitAsync(CancellationToken cancellationToken = default) { CommitCount++; return Task.CompletedTask; }
        public void Rollback() { RollbackCount++; if (RollbackFailure is not null) throw RollbackFailure; }
        public Task RollbackAsync(CancellationToken cancellationToken = default) { RollbackCount++; return RollbackFailure is null ? Task.CompletedTask : Task.FromException(RollbackFailure); }
        public void Dispose() => DisposeCount++;
        public ValueTask DisposeAsync() { DisposeCount++; return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure); }
        public DbTransaction GetDbTransaction() => throw new NotSupportedException();
        public void CreateSavepoint(string name) => throw new NotSupportedException();
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void RollbackToSavepoint(string name) => throw new NotSupportedException();
        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseSavepoint(string name) => throw new NotSupportedException();
        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
