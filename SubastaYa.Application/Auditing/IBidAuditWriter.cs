using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Auditing;

public interface IBidAuditWriter
{
    Task WriteAsync(
        AuditoriaLog auditoria,
        CancellationToken cancellationToken = default);
}
