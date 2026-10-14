using SubastaYa.Domain.Exceptions;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Application.Exceptions;
using SubastaYa.Domain.Rules;
using SubastaYa.Domain.Models;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Abstractions.Auditing;


namespace SubastaYa.Application.Features.Bids.Commands.PlaceBid;

public sealed class PlaceBidCommandHandler
    : ICommandHandler<PlaceBidCommand, PlaceBidResult>
{
    private readonly IPlaceBidRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly IBidNotifier _bidNotifier;
    private readonly IBidAuditWriter _bidAuditWriter;

    public PlaceBidCommandHandler(
    IPlaceBidRepository repository,
    TimeProvider timeProvider,
    IBidNotifier bidNotifier,
    IBidAuditWriter bidAuditWriter)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _bidNotifier = bidNotifier;
        _bidAuditWriter = bidAuditWriter;
    }
    public async Task<PlaceBidResult> HandleAsync(
    PlaceBidCommand command,
    CancellationToken cancellationToken = default)
    {
        try
        {
            BidRules.ValidarMonto(command.Monto);
        }
        catch (BidRuleException exception)
        {
            await RegistrarRechazoAsync(
                command,
                exception);

            throw;
        }

        var ahora = _timeProvider.GetUtcNow();

        await _repository.BeginTransactionAsync(cancellationToken);

        PlaceBidResult resultado;

        try
        {
            var (subasta, monto) = await ValidarPujaInicialAsync(
                command,
                ahora,
                cancellationToken);

            var (pujaAnterior, billetera, montoARequerir) =
                await ValidarFondosAsync(
                    command,
                    subasta,
                    monto,
                    cancellationToken);

            var nuevaPujaId = Guid.NewGuid();

            await LiberarFondosGanadorAnteriorAsync(
                command,
                pujaAnterior,
                nuevaPujaId,
                ahora,
                cancellationToken);

            await DesmarcarPujaGanadoraAnteriorAsync(
                pujaAnterior,
                cancellationToken);

            RetenerFondosNuevoGanador(
                command,
                billetera,
                montoARequerir,
                nuevaPujaId,
                ahora);

            var (nuevaPuja, subastaExtendida) = RegistrarNuevaPuja(
                command,
                subasta,
                monto,
                nuevaPujaId,
                ahora);

            await _repository.SaveChangesAsync(cancellationToken);

            await _repository.CommitTransactionAsync(cancellationToken);

            resultado = ConstruirResultado(
                subasta,
                nuevaPuja,
                subastaExtendida);
        }
        catch (BidPersistenceConcurrencyException exception)
        {
            try
            {
                await _repository.RollbackTransactionAsync(
                    CancellationToken.None);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "Se produjo un conflicto de concurrencia y falló el rollback. " +
                    "No se puede confirmar que la transacción haya sido revertida.",
                    exception,
                    rollbackFailure);
            }

            var conflicto = new BidConcurrencyException(
                exception.Message.Contains(
                    "Otra puja tomó el liderazgo",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Otra puja tomó el liderazgo mientras se procesaba la solicitud. Actualizá los datos e intentá nuevamente."
                    : "La subasta cambió mientras se procesaba la puja. Actualizá los datos e intentá nuevamente.");

            await RegistrarRechazoAsync(
                command,
                conflicto);

            throw conflicto;
        }
        catch (BidRuleException exception)
        {
            try
            {
                await _repository.RollbackTransactionAsync(
                    CancellationToken.None);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "La puja fue rechazada, pero falló el rollback. " +
                    "No se puede confirmar que la transacción haya sido revertida.",
                    exception,
                    rollbackFailure);
            }

            await RegistrarRechazoAsync(
                command,
                exception);

            throw;
        }
        catch
        {
            try
            {
                await _repository.RollbackTransactionAsync(
                    CancellationToken.None);
            }
            catch
            {
                // Conservamos la excepción original.
            }

            throw;
        }

        // La transacción ya fue confirmada.
        // SignalR no debe formar parte del bloque transaccional.
        await _bidNotifier.NotifyBidPlacedAsync(
            command.SubastaId,
            resultado,
            CancellationToken.None);

        return resultado;
    }

    private async Task<(Subasta Subasta, decimal Monto)> ValidarPujaInicialAsync(
    PlaceBidCommand command,
    DateTimeOffset ahora,
    CancellationToken cancellationToken)
    {
        var monto = BidRules.ValidarMonto(command.Monto);

        var subasta = await _repository.GetAuctionAsync(
            command.SubastaId,
            cancellationToken);

        if (subasta is null)
        {
            throw new BidNotFoundException(
                "AUCTION_NOT_FOUND",
                "La subasta no existe.");
        }

        BidRules.ValidarSubastaParaPujar(
            subasta,
            command.UsuarioId,
            ahora);

        BidRules.ValidarPujaMinima(subasta, monto);

        return (subasta, monto);
    }

    private async Task<(
    Puja? PujaAnterior,
    Billetera Billetera,
    decimal MontoARequerir)> ValidarFondosAsync(
    PlaceBidCommand command,
    Subasta subasta,
    decimal monto,
    CancellationToken cancellationToken)
    {
        var pujasGanadoras = await _repository.GetWinningBidsAsync(
            command.SubastaId,
            cancellationToken);

        var existenPujas = pujasGanadoras.Count > 0 ||
            await _repository.HasAnyBidAsync(
                command.SubastaId,
                cancellationToken);

        var pujaAnterior = BidRules.ValidarConsistenciaPujas(
            subasta,
            pujasGanadoras,
            existenPujas);

        var billetera = await _repository.GetWalletAsync(
            command.UsuarioId,
            cancellationToken);

        var montoARequerir = BidRules.ValidarFondosParaPujar(
            billetera,
            pujaAnterior,
            command.UsuarioId,
            monto);

        return (pujaAnterior, billetera!, montoARequerir);
    }

    private async Task LiberarFondosGanadorAnteriorAsync(
    PlaceBidCommand command,
    Puja? pujaAnterior,
    Guid nuevaPujaId,
    DateTimeOffset ahora,
    CancellationToken cancellationToken)
    {
        if (pujaAnterior is null ||
            pujaAnterior.UsuarioId == command.UsuarioId)
        {
            return;
        }

        var billeteraAnterior = await _repository.GetWalletAsync(
            pujaAnterior.UsuarioId,
            cancellationToken);

        BidRules.ValidarBilleteraGanadorAnterior(
            billeteraAnterior,
            pujaAnterior);

        var movimiento = BidRules.LiberarFondos(
            billeteraAnterior!,
            pujaAnterior,
            nuevaPujaId,
            command.SubastaId,
            ahora);

        _repository.AddLedgerTransaction(movimiento);
    }

    private void RetenerFondosNuevoGanador(
    PlaceBidCommand command,
    Billetera billetera,
    decimal montoARequerir,
    Guid nuevaPujaId,
    DateTimeOffset ahora)
    {
        var movimiento = BidRules.RetenerFondos(
            billetera,
            montoARequerir,
            nuevaPujaId,
            command.SubastaId,
            ahora);

        _repository.AddLedgerTransaction(movimiento);
    }

    private (Puja NuevaPuja, bool SubastaExtendida) RegistrarNuevaPuja(
    PlaceBidCommand command,
    Subasta subasta,
    decimal monto,
    Guid nuevaPujaId,
    DateTimeOffset ahora)
    {
        var nuevaPuja = new Puja
        {
            Id = nuevaPujaId,
            SubastaId = command.SubastaId,
            UsuarioId = command.UsuarioId,
            Monto = monto,
            EsGanadora = true,
            FechaUtc = ahora
        };

        _repository.AddBid(nuevaPuja);

        subasta.PrecioActual = monto;

        var auditoriaExtension = BidRules.ExtenderSubastaSiCorresponde(
            subasta,
            nuevaPujaId,
            monto,
            ahora);

        if (auditoriaExtension is not null)
        {
            _repository.AddAuditLog(auditoriaExtension);
        }

        var auditoriaPuja = BidRules.CrearAuditoriaPujaConfirmada(
            command.SubastaId,
            nuevaPujaId,
            command.UsuarioId,
            monto,
            ahora);

        _repository.AddAuditLog(auditoriaPuja);

        return (nuevaPuja, auditoriaExtension is not null);
    }

    private async Task DesmarcarPujaGanadoraAnteriorAsync(
    Puja? pujaAnterior,
    CancellationToken cancellationToken)
    {
        if (pujaAnterior is null)
        {
            return;
        }

        pujaAnterior.EsGanadora = false;

        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static PlaceBidResult ConstruirResultado(
    Subasta subasta,
    Puja nuevaPuja,
    bool subastaExtendida)
    {
        return new PlaceBidResult
        {
            Id = nuevaPuja.Id,
            SubastaId = nuevaPuja.SubastaId,
            Monto = nuevaPuja.Monto,
            Pseudonimo = nuevaPuja.UsuarioId.ToString()[..8],
            FechaUtc = nuevaPuja.FechaUtc,
            PrecioActual = subasta.PrecioActual,
            FechaFinUtc = subasta.FechaFinUtc,
            SubastaExtendida = subastaExtendida,
            Estado = "Liderando",
            IncrementoMinimo = subasta.IncrementoMinimo,
            PujaMinimaSiguiente =
                subasta.PrecioActual + subasta.IncrementoMinimo
        };
    }

    private async Task RegistrarRechazoAsync(
    PlaceBidCommand command,
    BidRuleException exception)
    {
        var auditoria = new AuditoriaLog
        {
            Id = Guid.NewGuid(),
            SubastaId = exception.Code == "AUCTION_NOT_FOUND"
            ? null
            : command.SubastaId,
            UsuarioId = command.UsuarioId,
            TipoEvento = exception.Code == "BID_CONFLICT"
                ? "BidConcurrencyConflict"
                : "BidRejected",
            Detalle =
                $"Puja rechazada. " +
                $"Código: {exception.Code}; " +
                $"usuario: {command.UsuarioId}; " +
                $"subasta: {command.SubastaId}; " +
                $"monto solicitado: {command.Monto}; " +
                $"motivo: {exception.Message}",
            FechaUtc = _timeProvider.GetUtcNow()
        };

        try
        {
            await _bidAuditWriter.WriteAsync(
                auditoria,
                CancellationToken.None);
        }
        catch (Exception auditFailure)
        {
            throw new BidAuditException(
                exception,
                auditFailure);
        }
    }
}