using SubastaYa.Application.Abstractions.Auditing;
using SubastaYa.Domain.Models;

namespace SubastaYa.API.Tests.TestInfrastructure;

public sealed class FakeBidAuditWriter : IBidAuditWriter
{
    public List<AuditoriaLog> Events { get; } = new();

    public bool SimulateWriteFailure { get; set; }

    public Task WriteAsync(
        AuditoriaLog auditoria,
        CancellationToken cancellationToken = default)
    {
        if (SimulateWriteFailure)
        {
            throw new InvalidOperationException(
                "Error simulado al guardar la auditoría.");
        }

        Events.Add(auditoria);

        return Task.CompletedTask;
    }
}