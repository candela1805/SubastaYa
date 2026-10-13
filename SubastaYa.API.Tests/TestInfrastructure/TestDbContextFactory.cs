using Microsoft.EntityFrameworkCore;

using SubastaYa.Infrastructure.Data;

namespace SubastaYa.API.Tests.TestInfrastructure;

internal sealed class TestDbContextFactory
    : IDbContextFactory<ApplicationDbContext>
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options)
    {
        _options = options;
    }

    public ApplicationDbContext CreateDbContext()
    {
        return new ApplicationDbContext(_options);
    }

    public Task<ApplicationDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(CreateDbContext());
    }
}