using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Wallet.DTOs;
using SubastaYa.Domain.Models;
using System.Runtime.ExceptionServices;

namespace SubastaYa.Application.Features.Wallet.Commands.DepositWallet;

public sealed class DepositWalletCommandHandler
    : ICommandHandler<DepositWalletCommand, WalletBalanceResult?>
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;
    public const int MaxIdempotencyKeyLength = 100;
    private readonly IWalletRepository _repository;
    private readonly TimeProvider _timeProvider;

    public DepositWalletCommandHandler(IWalletRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<WalletBalanceResult?> HandleAsync(
        DepositWalletCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);

        var idempotencyKey = NormalizeIdempotencyKey(command);

        await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            var wallet = await _repository.GetTrackedWalletAsync(command.UserId, cancellationToken);
            if (wallet is null)
            {
                await RollbackOrThrowUncertainAsync(
                    null,
                    "No pudo confirmarse la reversión del depósito sin billetera.");
                return null;
            }

            if (idempotencyKey is not null)
            {
                var existing = await _repository
                    .GetLedgerTransactionByIdempotencyKeyAsync(
                        idempotencyKey,
                        cancellationToken);

                if (existing is not null)
                {
                    EnsureIdempotentRequestMatches(
                        existing,
                        wallet.Id,
                        command.Amount);
                    await RollbackOrThrowUncertainAsync(
                        null,
                        "No pudo confirmarse la reversión del depósito duplicado.");
                    return ToBalance(wallet);
                }
            }

            if (wallet.SaldoTotal > MaxMoneyAmount - command.Amount ||
                wallet.SaldoDisponible > MaxMoneyAmount - command.Amount)
            {
                throw new WalletLimitExceededException(
                    "El depósito supera el límite monetario permitido para la billetera.");
            }

            var now = _timeProvider.GetUtcNow();
            wallet.SaldoTotal += command.Amount;
            wallet.SaldoDisponible += command.Amount;
            var movement = new TransaccionLedger
            {
                Id = Guid.NewGuid(),
                BilleteraId = wallet.Id,
                Tipo = TipoMovimientoBilletera.Deposito,
                Monto = command.Amount,
                Descripcion = "Depósito de saldo",
                ClaveIdempotencia = idempotencyKey,
                FechaUtc = now
            };
            _repository.AddLedgerTransaction(movement);
            _repository.AddAuditLog(new AuditoriaLog
            {
                Id = Guid.NewGuid(),
                UsuarioId = command.UserId,
                BilleteraId = wallet.Id,
                TipoEvento = "WalletDeposit",
                Detalle = $"Depósito confirmado. Usuario: {command.UserId}; billetera: {wallet.Id}; movimiento: {movement.Id}; importe: {command.Amount:F2}.",
                ClaveIdempotencia = idempotencyKey,
                FechaUtc = now
            });
            await _repository.SaveChangesAsync(cancellationToken);
            await _repository.CommitAsync(cancellationToken);
            return ToBalance(wallet);
        }
        catch (WalletTransactionStateUnknownException)
        {
            throw;
        }
        catch (WalletConcurrencyException exception)
        {
            await RollbackOrThrowUncertainAsync(
                exception,
                "No pudo confirmarse la reversión posterior al conflicto de concurrencia de la billetera.");

            var duplicate = await ResolveCommittedDuplicateAsync(
                command,
                idempotencyKey,
                CancellationToken.None);
            if (duplicate is not null)
                return duplicate;

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        catch (WalletIdempotencyPersistenceException exception)
        {
            await RollbackOrThrowUncertainAsync(
                exception,
                "No pudo confirmarse la reversión posterior al conflicto de idempotencia del depósito.");

            var duplicate = await ResolveCommittedDuplicateAsync(
                command,
                idempotencyKey,
                CancellationToken.None);
            if (duplicate is not null)
                return duplicate;

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        catch (Exception exception)
        {
            await RollbackOrThrowUncertainAsync(
                exception,
                "No pudo confirmarse la reversión del depósito.");
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private static void Validate(DepositWalletCommand command)
    {
        if (command.Amount <= 0)
            throw new WalletValidationException(
                "El monto del depósito debe ser mayor a 0.");

        if (decimal.Round(command.Amount, 2) != command.Amount)
            throw new WalletValidationException(
                "El monto del depósito debe tener como máximo dos decimales.");

        if (command.Amount > MaxMoneyAmount)
            throw new WalletLimitExceededException(
                "El depósito supera el límite monetario permitido para la billetera.");

        if (command.IdempotencyKey is not null &&
            string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new WalletValidationException(
                "La clave de idempotencia no puede estar vacía.");
        }

        if (command.IdempotencyKey?.Trim().Length > MaxIdempotencyKeyLength)
            throw new WalletValidationException(
                $"La clave de idempotencia no puede superar {MaxIdempotencyKeyLength} caracteres.");
    }

    private static string? NormalizeIdempotencyKey(
        DepositWalletCommand command) =>
        command.IdempotencyKey is null
            ? null
            : $"wallet:deposit:{command.UserId:N}:{command.IdempotencyKey.Trim()}";

    private static void EnsureIdempotentRequestMatches(
        TransaccionLedger transaction,
        Guid walletId,
        decimal amount)
    {
        if (transaction.BilleteraId != walletId ||
            transaction.Tipo != TipoMovimientoBilletera.Deposito ||
            transaction.Monto != amount)
        {
            throw new WalletIdempotencyConflictException(
                "La clave de idempotencia ya fue utilizada con un depósito diferente.");
        }
    }

    private async Task<WalletBalanceResult?> ResolveCommittedDuplicateAsync(
        DepositWalletCommand command,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is null)
            return null;

        var existing = await _repository
            .GetLedgerTransactionByIdempotencyKeyAsync(
                idempotencyKey,
                cancellationToken);
        if (existing is null)
            return null;

        var wallet = await _repository.GetTrackedWalletAsync(
            command.UserId,
            cancellationToken);
        if (wallet is null)
            return null;

        EnsureIdempotentRequestMatches(
            existing,
            wallet.Id,
            command.Amount);
        return ToBalance(wallet);
    }

    private async Task RollbackOrThrowUncertainAsync(
        Exception? operationException,
        string message)
    {
        try
        {
            await _repository.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            throw new WalletTransactionStateUnknownException(
                message,
                operationException,
                rollbackException);
        }
    }

    private static WalletBalanceResult ToBalance(Billetera wallet) =>
        new(wallet.SaldoTotal, wallet.SaldoRetenido, wallet.SaldoDisponible);
}

public sealed class WalletValidationException : Exception
{
    public WalletValidationException(string message) : base(message) { }
}

public sealed class WalletLimitExceededException : Exception
{
    public WalletLimitExceededException(string message) : base(message) { }
}

public sealed class WalletConcurrencyException : Exception
{
    public WalletConcurrencyException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class WalletIdempotencyConflictException : Exception
{
    public WalletIdempotencyConflictException(string message) : base(message) { }
}

public sealed class WalletIdempotencyPersistenceException : Exception
{
    public WalletIdempotencyPersistenceException(Exception inner)
        : base("La clave de idempotencia del depósito ya fue registrada.", inner) { }
}

public sealed class WalletTransactionStateUnknownException : Exception
{
    public WalletTransactionStateUnknownException(
        string message,
        Exception? operationException,
        Exception rollbackException)
        : base(
            message,
            operationException is null
                ? rollbackException
                : new AggregateException(operationException, rollbackException))
    {
        OperationException = operationException;
        RollbackException = rollbackException;
    }

    public Exception? OperationException { get; }
    public Exception RollbackException { get; }
}
