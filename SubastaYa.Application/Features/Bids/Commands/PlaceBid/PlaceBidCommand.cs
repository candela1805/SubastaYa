namespace SubastaYa.Application.Features.Bids.Commands.PlaceBid;

public sealed record PlaceBidCommand(
    Guid SubastaId,
    Guid UsuarioId,
    decimal? Monto
);
