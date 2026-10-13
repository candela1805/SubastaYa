using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Repositories;

namespace SubastaYa.API.Tests.CQRS;

public sealed class CloseAuctionTransactionSafetyTests
{
    [Fact]
    public async Task SkippedClosing_ConfirmsRollbackBeforeReturning()
    {
        var repository = new FakeClosingRepository();
        var auctionId = Guid.NewGuid();

        var result = await CreateHandler(repository).HandleAsync(
            new CloseAuctionCommand(auctionId));

        Assert.Equal(AuctionClosingOutcome.Skipped, result.Outcome);
        Assert.Equal(auctionId, result.AuctionId);
        Assert.Equal(1, repository.RollbackCount);
    }

    [Fact]
    public async Task ConcurrencyConflict_WhenRollbackFails_ReportsUnknownState()
    {
        var concurrencyFailure = new ClosingPersistenceConcurrencyException(
            new InvalidOperationException("stale row version"));
        var rollbackFailure = new InvalidOperationException("rollback failed");
        var repository = new FakeClosingRepository
        {
            ReadFailure = concurrencyFailure,
            RollbackFailure = rollbackFailure
        };
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<
            ClosingTransactionStateUnknownException>(
            () => handler.HandleAsync(new CloseAuctionCommand(Guid.NewGuid())));

        Assert.Same(concurrencyFailure, exception.OperationException);
        Assert.Same(rollbackFailure, exception.RollbackException);
        Assert.Equal(1, repository.RollbackCount);
    }

    [Fact]
    public async Task ConcurrencyConflict_WithConfirmedRollback_IsRecoverable()
    {
        var repository = new FakeClosingRepository
        {
            ReadFailure = new ClosingPersistenceConcurrencyException(
                new InvalidOperationException("stale row version"))
        };
        var auctionId = Guid.NewGuid();

        var result = await CreateHandler(repository).HandleAsync(
            new CloseAuctionCommand(auctionId));

        Assert.Equal(AuctionClosingOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(auctionId, result.AuctionId);
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
        var lifetime = new AuctionClosingTransaction(
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
        var lifetime = new AuctionClosingTransaction(
            transaction,
            _ => { });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifetime.RollbackAsync(CancellationToken.None));

        Assert.Same(rollbackFailure, exception);
        Assert.Equal(1, transaction.RollbackCount);
        Assert.Equal(1, transaction.DisposeCount);
    }

    private static CloseAuctionCommandHandler CreateHandler(
        IAuctionClosingRepository repository) =>
        new(repository, new NoOpAuctionNotifier(), TimeProvider.System);

    private sealed class NoOpAuctionNotifier : IAuctionNotifier
    {
        public Task NotifyStateChangedAsync(
            Guid auctionId,
            EstadoSubasta state,
            DateTimeOffset endDateUtc,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeClosingRepository : IAuctionClosingRepository
    {
        public Exception? ReadFailure { get; init; }
        public Exception? RollbackFailure { get; init; }
        public int RollbackCount { get; private set; }

        public Task BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Subasta?> GetAuctionAsync(
            Guid auctionId,
            CancellationToken cancellationToken)
        {
            if (ReadFailure is not null)
                return Task.FromException<Subasta?>(ReadFailure);

            return Task.FromResult<Subasta?>(null);
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            RollbackCount++;
            return RollbackFailure is null
                ? Task.CompletedTask
                : Task.FromException(RollbackFailure);
        }

        public Task<bool> HasSettlementAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Puja>> GetWinningBidsAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasBidsAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<decimal>> GetBidAmountsAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Billetera?> GetWalletAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void AddSettlement(LiquidacionSubasta settlement) => throw new NotSupportedException();
        public void AddLedgerTransactions(params TransaccionLedger[] transactions) => throw new NotSupportedException();
        public void AddAudit(AuditoriaLog audit) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CommitAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsAlreadyProcessedAsync(Guid auctionId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public void Rollback()
        {
            RollbackCount++;
            if (RollbackFailure is not null)
                throw RollbackFailure;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return RollbackFailure is null
                ? Task.CompletedTask
                : Task.FromException(RollbackFailure);
        }

        public void Dispose() => DisposeCount++;
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeFailure);
        }

        public DbTransaction GetDbTransaction() => throw new NotSupportedException();
        public void CreateSavepoint(string name) => throw new NotSupportedException();
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void RollbackToSavepoint(string name) => throw new NotSupportedException();
        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseSavepoint(string name) => throw new NotSupportedException();
        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
