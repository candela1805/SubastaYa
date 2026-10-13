using Microsoft.EntityFrameworkCore.Storage;
using System.Runtime.ExceptionServices;

namespace SubastaYa.Infrastructure.Repositories;

internal sealed class AuctionClosingTransaction
{
    private readonly IDbContextTransaction _transaction;
    private readonly Action<Exception> _reportDisposalFailure;

    public AuctionClosingTransaction(
        IDbContextTransaction transaction,
        Action<Exception> reportDisposalFailure)
    {
        _transaction = transaction;
        _reportDisposalFailure = reportDisposalFailure;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction.CommitAsync(cancellationToken);
        await DisposeAfterConfirmedCompletionAsync();
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
            await DisposeAfterConfirmedCompletionAsync();
        }

        if (rollbackFailure is not null)
        {
            ExceptionDispatchInfo.Capture(rollbackFailure).Throw();
        }
    }

    private async Task DisposeAfterConfirmedCompletionAsync()
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
                // La confirmación o reversión ya terminó. Un fallo del canal
                // de diagnóstico no puede cambiar el resultado transaccional.
            }
        }
    }
}
