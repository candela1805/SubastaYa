using SubastaYa.Dominio.Models;

namespace SubastaYa.Aplicacion.Repositories;

public interface ISubastaRepository
{
    void Agregar(Subasta subasta);

    void AgregarAuditoria(AuditoriaLog auditoria);

    Task GuardarCambiosAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subasta>> ObtenerPorVendedorAsync(
        Guid vendedorId,
        CancellationToken cancellationToken = default);

    Task<Subasta?> ObtenerPorIdAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<Subasta?> ObtenerDelVendedorAsync(
        Guid subastaId,
        Guid vendedorId,
        CancellationToken cancellationToken = default);

    Task<bool> TienePujasAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<bool> TieneLiquidacionAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task PrepararEliminacionAsync(
        Subasta subasta,
        CancellationToken cancellationToken = default);
}
