using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions.Auditing;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;

namespace SubastaYa.Infrastructure.Auditing;

public sealed class BidAuditWriter : IBidAuditWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

    public BidAuditWriter(
        IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task WriteAsync(
        AuditoriaLog auditoria,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.AuditoriaLogs.Add(auditoria);

        await context.SaveChangesAsync(cancellationToken);
    }
}