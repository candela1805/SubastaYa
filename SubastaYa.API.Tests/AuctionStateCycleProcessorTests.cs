using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SubastaYa.API.Data;
using SubastaYa.API.Hubs;
using SubastaYa.API.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Tests.TestInfrastructure;
using SubastaYa.API.Workers;

namespace SubastaYa.API.Tests;

public sealed class AuctionStateCycleProcessorTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        9,
        15,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task Cycle_respects_batch_size_and_expiration_order()
    {
        await using var host = CreateHost();
        var auctionIds = new[]
        {
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000002"),
            Guid.Parse("70000000-0000-0000-0000-000000000003"),
            Guid.Parse("70000000-0000-0000-0000-000000000004")
        };

        await host.SeedAsync(
            CreateAuction(auctionIds[0], Now.AddMinutes(-4)),
            CreateAuction(auctionIds[1], Now.AddMinutes(-3)),
            CreateAuction(auctionIds[2], Now.AddMinutes(-2)),
            CreateAuction(auctionIds[3], Now.AddMinutes(-1)));

        var result = await host.Processor.ProcessCycleAsync(2);

        Assert.Equal(2, result.ExpiredAuctionsFound);
        Assert.Equal(2, result.AuctionsFinalized);
        Assert.Equal(auctionIds.Take(2), host.Probe.ProcessedAuctionIds);
    }

    [Fact]
    public async Task Cycle_isolates_one_failure_and_counts_other_outcomes()
    {
        await using var host = CreateHost();
        var failedId = Guid.Parse(
            "71000000-0000-0000-0000-000000000001");
        var desertedId = Guid.Parse(
            "71000000-0000-0000-0000-000000000002");
        var skippedId = Guid.Parse(
            "71000000-0000-0000-0000-000000000003");

        await host.SeedAsync(
            CreateAuction(failedId, Now.AddMinutes(-3)),
            CreateAuction(desertedId, Now.AddMinutes(-2)),
            CreateAuction(skippedId, Now.AddMinutes(-1)));
        host.Probe.Failures.Add(failedId);
        host.Probe.Outcomes[desertedId] =
            AuctionClosingOutcome.Deserted;
        host.Probe.Outcomes[skippedId] =
            AuctionClosingOutcome.AlreadyProcessed;

        var result = await host.Processor.ProcessCycleAsync(10);

        Assert.Equal(3, result.ExpiredAuctionsFound);
        Assert.Equal(0, result.AuctionsFinalized);
        Assert.Equal(1, result.AuctionsDeserted);
        Assert.Equal(1, result.AuctionsSkipped);
        Assert.Equal(1, result.Errors);
        Assert.Equal(
            new[] { failedId, desertedId, skippedId },
            host.Probe.ProcessedAuctionIds);
    }

    [Fact]
    public async Task Cycle_stops_starting_work_after_cancellation()
    {
        await using var host = CreateHost();
        var firstId = Guid.Parse(
            "72000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse(
            "72000000-0000-0000-0000-000000000002");
        using var cancellation = new CancellationTokenSource();

        await host.SeedAsync(
            CreateAuction(firstId, Now.AddMinutes(-2)),
            CreateAuction(secondId, Now.AddMinutes(-1)));
        host.Probe.OnProcessed = _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Processor.ProcessCycleAsync(
                10,
                cancellation.Token));

        Assert.Equal(new[] { firstId }, host.Probe.ProcessedAuctionIds);
    }

    [Fact]
    public async Task Cycle_activates_only_due_scheduled_auctions()
    {
        await using var host = CreateHost();
        var auctionId = Guid.Parse(
            "73000000-0000-0000-0000-000000000001");
        var auction = CreateAuction(
            auctionId,
            Now.AddHours(1),
            EstadoSubasta.Programada);
        auction.FechaInicioUtc = Now.AddMinutes(-1);
        await host.SeedAsync(auction);

        var result = await host.Processor.ProcessCycleAsync(10);

        await using var context = host.CreateContext();
        var storedAuction = await context.Subastas
            .AsNoTracking()
            .SingleAsync(item => item.Id == auctionId);
        var audit = await context.AuditoriaLogs
            .AsNoTracking()
            .SingleAsync(item => item.SubastaId == auctionId);

        Assert.Equal(1, result.ScheduledAuctionsFound);
        Assert.Equal(1, result.AuctionsActivated);
        Assert.Equal(EstadoSubasta.Activa, storedAuction.Estado);
        Assert.Equal("AuctionStateChanged", audit.TipoEvento);
        Assert.Empty(host.Probe.ProcessedAuctionIds);
    }

    [Fact]
    public async Task Cycle_rejects_non_positive_batch_size()
    {
        await using var host = CreateHost();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            host.Processor.ProcessCycleAsync(0));
    }

    private static CycleTestHost CreateHost()
    {
        var timeProvider = new FixedTimeProvider(Now);
        var probe = new ClosingProbe();
        var services = new ServiceCollection();
        var databaseName = $"cycle-tests-{Guid.NewGuid()}";

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(probe);
        services.AddSingleton<IHubContext<AuctionHub>>(
            NoOpHubContext.Instance);
        services.AddLogging();
        services.AddScoped<IAuctionService, AuctionService>();
        services.AddScoped<IAuctionClosingService, ProbeClosingService>();

        return new CycleTestHost(
            services.BuildServiceProvider(),
            timeProvider,
            probe);
    }

    private static Subasta CreateAuction(
        Guid id,
        DateTimeOffset endDateUtc,
        EstadoSubasta state = EstadoSubasta.Activa)
    {
        return new Subasta
        {
            Id = id,
            Titulo = $"Subasta {id}",
            Descripcion = "Subasta para probar el ciclo automático.",
            Categoria = "Pruebas",
            PrecioInicial = 100m,
            PrecioActual = 100m,
            IncrementoMinimo = 10m,
            FechaInicioUtc = Now.AddHours(-1),
            FechaFinUtc = endDateUtc,
            Estado = state
        };
    }

    private sealed class CycleTestHost : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public CycleTestHost(
            ServiceProvider serviceProvider,
            TimeProvider timeProvider,
            ClosingProbe probe)
        {
            _serviceProvider = serviceProvider;
            Probe = probe;
            Processor = new AuctionStateCycleProcessor(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                timeProvider,
                NullLogger<AuctionStateCycleProcessor>.Instance);
        }

        public ClosingProbe Probe { get; }

        public AuctionStateCycleProcessor Processor { get; }

        public ApplicationDbContext CreateContext()
        {
            return _serviceProvider
                .GetRequiredService<ApplicationDbContext>();
        }

        public async Task SeedAsync(params Subasta[] auctions)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var context = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();
            context.Subastas.AddRange(auctions);
            await context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync()
        {
            return _serviceProvider.DisposeAsync();
        }
    }

    private sealed class ClosingProbe
    {
        public List<Guid> ProcessedAuctionIds { get; } = [];

        public HashSet<Guid> Failures { get; } = [];

        public Dictionary<Guid, AuctionClosingOutcome> Outcomes { get; } =
            [];

        public Action<Guid>? OnProcessed { get; set; }

        public AuctionClosingResult Process(Guid auctionId)
        {
            ProcessedAuctionIds.Add(auctionId);
            OnProcessed?.Invoke(auctionId);

            if (Failures.Contains(auctionId))
            {
                throw new InvalidOperationException(
                    $"Fallo controlado para {auctionId}.");
            }

            var outcome = Outcomes.GetValueOrDefault(
                auctionId,
                AuctionClosingOutcome.Finalized);
            return new AuctionClosingResult(auctionId, outcome);
        }
    }

    private sealed class ProbeClosingService : IAuctionClosingService
    {
        private readonly ClosingProbe _probe;

        public ProbeClosingService(ClosingProbe probe)
        {
            _probe = probe;
        }

        public Task<AuctionClosingResult> ProcessAsync(
            Guid auctionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_probe.Process(auctionId));
        }
    }
}
