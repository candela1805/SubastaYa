using SubastaYa.API.Contracts.Auctions;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public interface IAuctionService
{
    Task<PagedAuctionResponse> ObtenerSubastasAsync(
        string? buscar,
        decimal? precioMin,
        decimal? precioMax,
        string? categoria,
        EstadoSubasta? estado,
        string? ordenar,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AuctionResponse> CrearSubastaAsync(
        Guid vendedorId,
        CreateAuctionRequest request,
        CancellationToken cancellationToken = default);

    Task ActivarAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);
}
