using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Workers;

namespace SubastaYa.API.Tests.TestInfrastructure;

internal sealed class BidApiFactory : WebApplicationFactory<Program>
{
    // InMemory se limita a coordinar dos SaveChanges sobre el mismo token.
    // La atomicidad y el rollback se prueban por separado con SQLite relacional.
    private readonly string _databaseName = $"bid-api-{Guid.NewGuid()}";
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private readonly FixedTimeProvider _timeProvider = new(
        new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero));

    public ConcurrentBidSaveInterceptor SaveInterceptor { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            var optionConfigurations = services
                .Where(descriptor =>
                    descriptor.ServiceType.IsGenericType &&
                    descriptor.ServiceType.GenericTypeArguments.Contains(
                        typeof(ApplicationDbContext)) &&
                    descriptor.ServiceType.Name.StartsWith(
                        "IDbContextOptionsConfiguration",
                        StringComparison.Ordinal))
                .ToList();
            foreach (var descriptor in optionConfigurations)
            {
                services.Remove(descriptor);
            }
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options
                    .UseInMemoryDatabase(_databaseName, _databaseRoot)
                    .ConfigureWarnings(warnings => warnings
                        .Ignore(InMemoryEventId.TransactionIgnoredWarning)
                        .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .AddInterceptors(SaveInterceptor);
            });

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_timeProvider);

            var worker = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(AuctionStateWorker));
            if (worker is not null)
            {
                services.Remove(worker);
            }

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme =
                        TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme =
                        TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<
                    AuthenticationSchemeOptions,
                    TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var seller = CreateUser(
            BidTestDatabase.SellerId,
            "seller-api@tests.local");
        var bidderOne = CreateUser(
            BidTestDatabase.BidderOneId,
            "bidder-one-api@tests.local");
        var bidderTwo = CreateUser(
            BidTestDatabase.BidderTwoId,
            "bidder-two-api@tests.local");

        context.Usuarios.AddRange(seller, bidderOne, bidderTwo);
        context.Billeteras.AddRange(
            CreateWallet(
                BidTestDatabase.BidderOneWalletId,
                bidderOne),
            CreateWallet(
                BidTestDatabase.BidderTwoWalletId,
                bidderTwo));
        context.Subastas.Add(new Subasta
        {
            Id = BidTestDatabase.AuctionId,
            VendedorId = seller.Id,
            Vendedor = seller,
            Titulo = "Subasta API concurrente",
            Descripcion = "Prueba HTTP del bloqueo optimista.",
            Categoria = "Pruebas",
            PrecioInicial = 100m,
            PrecioActual = 100m,
            IncrementoMinimo = 10m,
            FechaInicioUtc = _timeProvider.GetUtcNow().AddMinutes(-1),
            FechaFinUtc = _timeProvider.GetUtcNow().AddMinutes(10),
            Estado = EstadoSubasta.Activa
        });

        await context.SaveChangesAsync();
    }

    private static Usuario CreateUser(Guid id, string email)
    {
        return new Usuario
        {
            Id = id,
            Email = email,
            Nombre = email,
            PasswordHash = "TEST_ONLY",
            FechaRegistro = DateTimeOffset.UnixEpoch
        };
    }

    private static Billetera CreateWallet(Guid id, Usuario user)
    {
        return new Billetera
        {
            Id = id,
            UsuarioId = user.Id,
            Usuario = user,
            SaldoTotal = 1_000m,
            SaldoRetenido = 0m,
            SaldoDisponible = 1_000m
        };
    }
}
