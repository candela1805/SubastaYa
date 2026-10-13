namespace SubastaYa.Application.Features.Bids.Queries.GetRoomState;

public sealed record GetRoomStateQuery(
    Guid SubastaId,
    Guid UsuarioId
);
