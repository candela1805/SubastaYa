using SubastaYa.Dominio.Models;

namespace SubastaYa.Aplicacion.Repositories;

public interface ISubastaRepository
{
    void Agregar(Subasta subasta);

    Task GuardarCambiosAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subasta>> ObtenerPorVendedorAsync(
        Guid vendedorId,
        CancellationToken cancellationToken = default);
}