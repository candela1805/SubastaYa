using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Features.Auctions.Commands;

public sealed record CreateAuctionCommand(Guid SellerId, string Title, string Description, string? ImageUrl, string Category, decimal InitialPrice, decimal MinimumIncrement, DateTimeOffset StartDateUtc, DateTimeOffset EndDateUtc);
public sealed record UpdateAuctionCommand(Guid AuctionId, Guid SellerId, string Title, string Description, string? ImageUrl, string Category, DateTimeOffset EndDateUtc);
public sealed record DeleteAuctionCommand(Guid AuctionId, Guid SellerId);
public sealed record ActivateAuctionCommand(Guid AuctionId);

public sealed class AuctionOperationConflictException : Exception
{
    public AuctionOperationConflictException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class AuctionValidationException : Exception
{
    public AuctionValidationException(string message) : base(message) { }
}

public static class AuctionCreationRules
{
    public const string TitleRequired = "El título es obligatorio.";
    public const string DescriptionRequired = "La descripción es obligatoria.";
    public const string CategoryRequired = "La categoría es obligatoria.";
    public const string InitialPriceMustBePositive =
        "El precio inicial debe ser mayor a 0.";
    public const string MinimumIncrementMustBePositive =
        "El incremento mínimo debe ser mayor a 0.";
    public const string EndDateMustBeAfterStartDate =
        "La fecha de finalización debe ser posterior a la fecha de inicio.";
    public const string EndDateMustBeFuture =
        "La fecha de finalización debe ser futura.";
}

public sealed class CreateAuctionCommandHandler : ICommandHandler<CreateAuctionCommand, AuctionResult>
{
    private readonly IAuctionRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CreateAuctionCommandHandler(
        IAuctionRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<AuctionResult> HandleAsync(CreateAuctionCommand command, CancellationToken cancellationToken = default)
    {
        Validate(command, _timeProvider.GetUtcNow());

        var auction = new Subasta
        {
            VendedorId = command.SellerId, Titulo = command.Title.Trim(), Descripcion = command.Description.Trim(),
            ImagenUrl = string.IsNullOrWhiteSpace(command.ImageUrl) ? null : command.ImageUrl.Trim(), Categoria = command.Category.Trim(),
            PrecioInicial = command.InitialPrice, PrecioActual = command.InitialPrice, IncrementoMinimo = command.MinimumIncrement,
            FechaInicioUtc = command.StartDateUtc, FechaFinUtc = command.EndDateUtc,
            Estado = command.StartDateUtc > _timeProvider.GetUtcNow() ? EstadoSubasta.Programada : EstadoSubasta.Activa
        };
        _repository.Add(auction);
        await _repository.SaveChangesAsync(cancellationToken);
        return Map(auction);
    }

    private static void Validate(
        CreateAuctionCommand command,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            throw new AuctionValidationException(
                AuctionCreationRules.TitleRequired);

        if (string.IsNullOrWhiteSpace(command.Description))
            throw new AuctionValidationException(
                AuctionCreationRules.DescriptionRequired);

        if (string.IsNullOrWhiteSpace(command.Category))
            throw new AuctionValidationException(
                AuctionCreationRules.CategoryRequired);

        if (command.InitialPrice <= 0)
            throw new AuctionValidationException(
                AuctionCreationRules.InitialPriceMustBePositive);

        if (command.MinimumIncrement <= 0)
            throw new AuctionValidationException(
                AuctionCreationRules.MinimumIncrementMustBePositive);

        if (command.EndDateUtc <= command.StartDateUtc)
            throw new AuctionValidationException(
                AuctionCreationRules.EndDateMustBeAfterStartDate);

        if (command.EndDateUtc <= now)
            throw new AuctionValidationException(
                AuctionCreationRules.EndDateMustBeFuture);
    }

    internal static AuctionResult Map(Subasta auction) => new(auction.Id, auction.Titulo, auction.Descripcion ?? string.Empty, auction.ImagenUrl, auction.Categoria, auction.PrecioInicial, auction.PrecioActual, auction.IncrementoMinimo, auction.FechaInicioUtc, auction.FechaFinUtc, auction.Estado);
}

public sealed class UpdateAuctionCommandHandler : ICommandHandler<UpdateAuctionCommand, AuctionResult>
{
    private readonly IAuctionRepository _repository;
    private readonly TimeProvider _timeProvider;
    public UpdateAuctionCommandHandler(IAuctionRepository repository, TimeProvider timeProvider) { _repository = repository; _timeProvider = timeProvider; }
    public async Task<AuctionResult> HandleAsync(UpdateAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var auction = await _repository.GetOwnedTrackedAsync(command.AuctionId, command.SellerId, cancellationToken) ?? throw new KeyNotFoundException("La publicación no existe o no pertenece al usuario autenticado.");
        if (auction.Estado is EstadoSubasta.Finalizada or EstadoSubasta.Desierta) throw new AuctionOperationConflictException("Las publicaciones finalizadas o desiertas no pueden editarse.");
        if (await _repository.HasBidsAsync(command.AuctionId, cancellationToken)) throw new AuctionOperationConflictException("La publicación no puede editarse porque ya recibió pujas.");
        if (command.EndDateUtc <= _timeProvider.GetUtcNow() || command.EndDateUtc <= auction.FechaInicioUtc) throw new AuctionOperationConflictException("La fecha de finalización debe ser futura y posterior al inicio.");
        auction.Titulo = command.Title.Trim(); auction.Descripcion = command.Description.Trim(); auction.Categoria = command.Category.Trim();
        auction.ImagenUrl = string.IsNullOrWhiteSpace(command.ImageUrl) ? null : command.ImageUrl.Trim(); auction.FechaFinUtc = command.EndDateUtc;
        _repository.AddAudit(new AuditoriaLog { Id = Guid.NewGuid(), SubastaId = auction.Id, TipoEvento = "AuctionUpdated", Detalle = "La publicación fue actualizada por su vendedor.", FechaUtc = _timeProvider.GetUtcNow() });
        try { await _repository.SaveChangesAsync(cancellationToken); }
        catch (AuctionPersistenceConcurrencyException ex) { throw new AuctionOperationConflictException("La publicación cambió durante la edición. Actualizá los datos e intentá nuevamente.", ex); }
        return CreateAuctionCommandHandler.Map(auction);
    }
}

public sealed class DeleteAuctionCommandHandler : ICommandHandler<DeleteAuctionCommand, bool>
{
    private readonly IAuctionRepository _repository;
    public DeleteAuctionCommandHandler(IAuctionRepository repository) => _repository = repository;
    public async Task<bool> HandleAsync(DeleteAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var auction = await _repository.GetOwnedTrackedAsync(command.AuctionId, command.SellerId, cancellationToken) ?? throw new KeyNotFoundException("La publicación no existe o no pertenece al usuario autenticado.");
        if (await _repository.HasBidsAsync(command.AuctionId, cancellationToken)) throw new AuctionOperationConflictException("Esta publicación no puede eliminarse porque ya recibió una puja.");
        if (await _repository.HasSettlementAsync(command.AuctionId, cancellationToken)) throw new AuctionOperationConflictException("Esta publicación no puede eliminarse porque tiene una liquidación asociada.");
        if (await _repository.HasAuditAsync(command.AuctionId, cancellationToken)) throw new AuctionOperationConflictException("Esta publicación no puede eliminarse porque posee historial de auditoría inmutable.");
        _repository.Remove(auction);
        try { await _repository.SaveChangesAsync(cancellationToken); }
        catch (AuctionPersistenceConcurrencyException ex) { throw new AuctionOperationConflictException("La publicación cambió durante la eliminación. Actualizá los datos e intentá nuevamente.", ex); }
        catch (AuctionPersistenceException ex) { throw new AuctionOperationConflictException("La publicación generó información histórica y ya no puede eliminarse.", ex); }
        return true;
    }
}

public sealed class ActivateAuctionCommandHandler : ICommandHandler<ActivateAuctionCommand, EstadoSubasta?>
{
    private readonly IAuctionRepository _repository; private readonly IAuctionNotifier _notifier; private readonly TimeProvider _timeProvider;
    public ActivateAuctionCommandHandler(IAuctionRepository repository, IAuctionNotifier notifier, TimeProvider timeProvider) { _repository = repository; _notifier = notifier; _timeProvider = timeProvider; }
    public async Task<EstadoSubasta?> HandleAsync(ActivateAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var auction = await _repository.GetTrackedAsync(command.AuctionId, cancellationToken) ?? throw new KeyNotFoundException("La subasta no existe.");
        var now = _timeProvider.GetUtcNow();
        if (auction.Estado != EstadoSubasta.Programada || auction.FechaInicioUtc > now) return auction.Estado;
        var previous = auction.Estado; auction.Estado = EstadoSubasta.Activa;
        _repository.AddAudit(new AuditoriaLog { Id = Guid.NewGuid(), SubastaId = auction.Id, TipoEvento = "AuctionStateChanged", Detalle = $"Estado cambiado de {previous} a {auction.Estado}", FechaUtc = now });
        await _repository.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyStateChangedAsync(auction.Id, auction.Estado, auction.FechaFinUtc, CancellationToken.None);
        return auction.Estado;
    }
}

public sealed class AuctionPersistenceConcurrencyException : Exception { public AuctionPersistenceConcurrencyException(Exception inner) : base("Conflicto de concurrencia al persistir la subasta.", inner) { } }
public sealed class AuctionPersistenceException : Exception { public AuctionPersistenceException(Exception inner) : base("Error al persistir la subasta.", inner) { } }
