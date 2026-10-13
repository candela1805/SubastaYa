using Microsoft.EntityFrameworkCore.Storage;
using System.Runtime.ExceptionServices;

namespace SubastaYa.Infrastructure.Repositories;

internal sealed class WalletTransaction
{
    private readonly IDbContextTransaction _transaction;
    private readonly Action<Exception> _reportDisposalFailure;

    public WalletTransaction(
        IDbContextTransaction transaction,
        Action<Exception> reportDisposalFailure)
    {
        _transaction = transaction;
        _reportDisposalFailure = reportDisposalFailure;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction.CommitAsync(cancellationToken);
        await DisposeAfterCompletionAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        Exception? rollbackFailure = null;

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            rollbackFailure = exception;
        }
        finally
        {
            await DisposeAfterCompletionAsync();
        }

        if (rollbackFailure is not null)
            ExceptionDispatchInfo.Capture(rollbackFailure).Throw();
    }

    private async Task DisposeAfterCompletionAsync()
    {
        try
        {
            await _transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            try
            {
                _reportDisposalFailure(exception);
            }
            catch
            {
                // El resultado financiero ya fue confirmado o revertido.
            }
        }
    }
}
