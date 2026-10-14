using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Domain.Rules;
using SubastaYa.Domain.Models;
using System.Runtime.ExceptionServices;

namespace SubastaYa.Application.Features.Auctions.Commands;

public sealed record CloseAuctionCommand(Guid AuctionId);
public enum AuctionClosingOutcome { Skipped, AlreadyProcessed, Deserted, Finalized, ConcurrencyConflict }
public sealed record AuctionClosingResult(Guid AuctionId, AuctionClosingOutcome Outcome, Guid? SettlementId = null);

public sealed class ClosingPersistenceConcurrencyException : Exception { public ClosingPersistenceConcurrencyException(Exception inner) : base("Conflicto de concurrencia durante el cierre.", inner) { } }
public sealed class ClosingIdempotencyException : Exception { public ClosingIdempotencyException(Exception inner) : base("El cierre ya fue persistido por otra instancia.", inner) { } }
public sealed class ClosingTransactionStateUnknownException : Exception
{
    public ClosingTransactionStateUnknownException(
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

public sealed class CloseAuctionCommandHandler : ICommandHandler<CloseAuctionCommand, AuctionClosingResult>
{
    private const string SettlementAuditPrefix = "auction:settled:";
    private const string DesertedAuditPrefix = "auction:deserted:";
    private readonly IAuctionClosingRepository _repository;
    private readonly IAuctionNotifier _notifier;
    private readonly TimeProvider _timeProvider;

    public CloseAuctionCommandHandler(IAuctionClosingRepository repository, IAuctionNotifier notifier, TimeProvider timeProvider)
    { _repository = repository; _notifier = notifier; _timeProvider = timeProvider; }

    public async Task<AuctionClosingResult> HandleAsync(CloseAuctionCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _repository.BeginTransactionAsync(cancellationToken);
        DateTimeOffset endDate;
        try
        {
            var now = _timeProvider.GetUtcNow();

            var auction = await _repository.GetAuctionAsync(
                command.AuctionId,
                cancellationToken);

            if (auction is null)
            {
                return await RollbackResult(
                    command.AuctionId,
                    AuctionClosingOutcome.Skipped);
            }

            var eligibility = AuctionClosingRules.EvaluarEstado(auction, now);

            if (eligibility == AuctionClosingEligibility.NotDue)
            {
                return await RollbackResult(
                    command.AuctionId,
                    AuctionClosingOutcome.Skipped);
            }

            if (eligibility == AuctionClosingEligibility.AlreadyProcessed)
            {
                return await RollbackResult(
                    command.AuctionId,
                    AuctionClosingOutcome.AlreadyProcessed);
            }

            var hasSettlement = await _repository.HasSettlementAsync(
                command.AuctionId,
                cancellationToken);

            AuctionClosingRules.ValidarSinLiquidacion(
                auction,
                hasSettlement);

            var winners = await _repository.GetWinningBidsAsync(
                command.AuctionId,
                cancellationToken);

            AuctionClosingRules.ValidarCantidadGanadoras(
                auction,
                winners.Count);

            endDate = auction.FechaFinUtc;
            if (winners.Count == 0)
            {
                var hasBids = await _repository.HasBidsAsync(
                    command.AuctionId,
                    cancellationToken);

                AuctionClosingRules.DeclararDesierta(
                    auction,
                    hasBids,
                    now);
                _repository.AddAudit(new AuditoriaLog { Id = Guid.NewGuid(), SubastaId = auction.Id, TipoEvento = "AuctionDeserted", Detalle = "Estado anterior: Activa; estado nuevo: Desierta; motivo: vencimiento sin ofertas; origen: AuctionStateWorker.", ClaveIdempotencia = $"{DesertedAuditPrefix}{auction.Id}", FechaUtc = now });
                await _repository.SaveChangesAsync(cancellationToken); await _repository.CommitAsync(cancellationToken);
                await _notifier.NotifyStateChangedAsync(auction.Id, auction.Estado, endDate, CancellationToken.None);
                return new(auction.Id, AuctionClosingOutcome.Deserted);
            }
            var winner = winners[0];

            var amounts = await _repository.GetBidAmountsAsync(
                auction.Id,
                cancellationToken);

            var sellerId = AuctionSettlementRules.ValidarGanador(
                auction,
                winner,
                amounts);
            var buyer = await _repository.GetWalletAsync(winner.UsuarioId, cancellationToken) ?? throw new InvalidOperationException($"El comprador de la subasta {auction.Id} no tiene billetera.");
            var seller = await _repository.GetWalletAsync(sellerId, cancellationToken) ?? throw new InvalidOperationException($"El vendedor de la subasta {auction.Id} no tiene billetera.");
            var amount = winner.Monto;

            AuctionSettlementRules.TransferirFondos(
                auction.Id,
                buyer,
                seller,
                amount);

            var settlement = new LiquidacionSubasta
            {
                Id = Guid.NewGuid(),
                SubastaId = auction.Id,
                PujaGanadoraId = winner.Id,
                CompradorId = winner.UsuarioId,
                VendedorId = sellerId,
                ImporteFinal = amount,
                FechaAdjudicacionUtc = now,
                FechaLiquidacionUtc = now
            };

            AuctionClosingRules.Finalizar(auction, now);
            _repository.AddSettlement(settlement);
            _repository.AddLedgerTransactions(
                new TransaccionLedger { Id = Guid.NewGuid(), BilleteraId = buyer.Id, LiquidacionSubastaId = settlement.Id, ClaveIdempotencia = $"auction:settlement:payment:{auction.Id}", Tipo = TipoMovimientoBilletera.Pago, Monto = amount, Descripcion = $"Pago de la liquidación {settlement.Id} por la subasta {auction.Id}.", FechaUtc = now },
                new TransaccionLedger { Id = Guid.NewGuid(), BilleteraId = seller.Id, LiquidacionSubastaId = settlement.Id, ClaveIdempotencia = $"auction:settlement:collection:{auction.Id}", Tipo = TipoMovimientoBilletera.Cobro, Monto = amount, Descripcion = $"Cobro de la liquidación {settlement.Id} por la subasta {auction.Id}.", FechaUtc = now });
            _repository.AddAudit(new AuditoriaLog { Id = Guid.NewGuid(), SubastaId = auction.Id, TipoEvento = "AuctionSettled", Detalle = $"Subasta adjudicada y liquidada. Liquidación: {settlement.Id}; puja: {winner.Id}; comprador: {winner.UsuarioId}; vendedor: {sellerId}; importe: {amount:F2}; origen: AuctionStateWorker.", ClaveIdempotencia = $"{SettlementAuditPrefix}{auction.Id}", FechaUtc = now });
            await _repository.SaveChangesAsync(cancellationToken); await _repository.CommitAsync(cancellationToken);
            await _notifier.NotifyStateChangedAsync(auction.Id, auction.Estado, endDate, CancellationToken.None);
            return new(auction.Id, AuctionClosingOutcome.Finalized, settlement.Id);
        }
        catch (ClosingTransactionStateUnknownException)
        {
            throw;
        }
        catch (ClosingPersistenceConcurrencyException exception)
        {
            await RollbackOrThrowUncertainAsync(
                exception,
                "No pudo confirmarse la reversión posterior al conflicto de concurrencia.");
            return new(command.AuctionId, AuctionClosingOutcome.ConcurrencyConflict);
        }
        catch (ClosingIdempotencyException exception)
        {
            await RollbackOrThrowUncertainAsync(
                exception,
                "No pudo confirmarse la reversión posterior al conflicto de idempotencia.");
            if (await _repository.IsAlreadyProcessedAsync(command.AuctionId, CancellationToken.None)) return new(command.AuctionId, AuctionClosingOutcome.AlreadyProcessed);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackOrThrowUncertainAsync(
                exception,
                "No pudo confirmarse la reversión del cierre de la subasta.");
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private async Task<AuctionClosingResult> RollbackResult(
        Guid id,
        AuctionClosingOutcome outcome)
    {
        await RollbackOrThrowUncertainAsync(
            null,
            "No pudo confirmarse la reversión del cierre omitido.");
        return new(id, outcome);
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
            throw new ClosingTransactionStateUnknownException(
                message,
                operationException,
                rollbackException);
        }
    }
}
