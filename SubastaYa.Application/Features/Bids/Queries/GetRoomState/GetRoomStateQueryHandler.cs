using SubastaYa.Domain.Exceptions;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Exceptions;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Domain.Rules;

namespace SubastaYa.Application.Features.Bids.Queries.GetRoomState;

public sealed class GetRoomStateQueryHandler
    : IQueryHandler<GetRoomStateQuery, BidRoomStateResult>
{
    private readonly IBidReadRepository _repository;

    public GetRoomStateQueryHandler(IBidReadRepository repository)
    {
        _repository = repository;
    }

    public async Task<BidRoomStateResult> HandleAsync(
        GetRoomStateQuery query,
        CancellationToken cancellationToken = default)
    {
        var subasta = await _repository.GetAuctionAsync(
            query.SubastaId,
            cancellationToken);

        if (subasta is null)
        {
            throw new BidNotFoundException(
                "AUCTION_NOT_FOUND",
                "La subasta no existe.");
        }

        var ganadores = await _repository.GetWinningBidUserIdsAsync(
            query.SubastaId,
            cancellationToken);

        if (ganadores.Count > 1)
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "La subasta tiene más de una puja ganadora registrada.");
        }

        if (ganadores.Count == 0 &&
            await _repository.HasAnyBidAsync(
                query.SubastaId,
                cancellationToken))
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "La subasta tiene pujas, pero ninguna está marcada como ganadora.");
        }

        var pujaMinima = BidRules.CalcularPujaMinima(subasta);

        var usuarioParticipo = ganadores.Count > 0 &&
            (ganadores[0] == query.UsuarioId ||
             await _repository.HasUserBidAsync(
                 query.SubastaId,
                 query.UsuarioId,
                 cancellationToken));

        var estadoPostor = !usuarioParticipo
            ? "SinPuja"
            : ganadores[0] == query.UsuarioId
                ? "Liderando"
                : "Superado";

        return new BidRoomStateResult
        {
            SubastaId = subasta.Id,
            PrecioActual = subasta.PrecioActual,
            IncrementoMinimo = subasta.IncrementoMinimo,
            PujaMinimaSiguiente = pujaMinima,
            FechaFinUtc = subasta.FechaFinUtc,
            EstadoSubasta = subasta.Estado,
            EstadoPostor = estadoPostor
        };
    }
}