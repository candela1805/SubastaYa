using SubastaYa.Aplicacion.Contracts.Auctions;
using SubastaYa.Dominio.Models;

namespace SubastaYa.Aplicacion.Repositories;

public interface ICatalogoRepository
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

    Task<IReadOnlyList<string>> ObtenerCategoriasAsync(
        CancellationToken cancellationToken = default);
    
}
