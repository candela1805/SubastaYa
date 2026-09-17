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

    Task<IReadOnlyList<string>> ObtenerCategoriasAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuctionResponse>> ObtenerPublicacionesAsync(
        Guid vendedorId,
        CancellationToken cancellationToken = default);

    Task<AuctionResponse> ActualizarPublicacionAsync(
        Guid subastaId,
        Guid vendedorId,
        UpdateAuctionRequest request,
        CancellationToken cancellationToken = default);

    Task EliminarPublicacionAsync(
        Guid subastaId,
        Guid vendedorId,
        CancellationToken cancellationToken = default);

    Task ActivarAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);
}
