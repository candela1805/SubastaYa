using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

namespace SubastaYa.API.Tests.TestInfrastructure;

internal sealed class BidTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private BidTestDatabase(
        SqliteConnection connection,
        DbContextOptions<ApplicationDbContext> options,
        DateTimeOffset now)
    {
        _connection = connection;
        Options = options;
        Now = now;
    }

    public static readonly Guid SellerId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid BidderOneId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    public static readonly Guid BidderTwoId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static readonly Guid AuctionId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");

    public static readonly Guid BidderOneWalletId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    public static readonly Guid BidderTwoWalletId =
        Guid.Parse("40000000-0000-0000-0000-000000000002");

    public DbContextOptions<ApplicationDbContext> Options { get; }

    public DateTimeOffset Now { get; }

    public static async Task<BidTestDatabase> CreateAsync(
        TimeSpan? remaining = null,
        EstadoSubasta auctionState = EstadoSubasta.Activa,
        decimal bidderOneBalance = 1_000m,
        decimal bidderTwoBalance = 1_000m)
    {
        var now = new DateTimeOffset(
            2026,
            9,
            14,
            18,
            0,
            0,
            TimeSpan.Zero);
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;
        var database = new BidTestDatabase(connection, options, now);

        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();

        var seller = CreateUser(SellerId, "seller@tests.local", "Vendedor");
        var bidderOne = CreateUser(
            BidderOneId,
            "bidder-one@tests.local",
            "Postor Uno");
        var bidderTwo = CreateUser(
            BidderTwoId,
            "bidder-two@tests.local",
            "Postor Dos");

        context.Usuarios.AddRange(seller, bidderOne, bidderTwo);
        context.Billeteras.AddRange(
            CreateWallet(
                BidderOneWalletId,
                bidderOne,
                bidderOneBalance),
            CreateWallet(
                BidderTwoWalletId,
                bidderTwo,
                bidderTwoBalance));
        context.Subastas.Add(new Subasta
        {
            Id = AuctionId,
            VendedorId = SellerId,
            Vendedor = seller,
            Titulo = "Notebook de prueba",
            Descripcion = "Subasta usada por las pruebas del motor.",
            Categoria = "Tecnología",
            PrecioInicial = 100m,
            PrecioActual = 100m,
            IncrementoMinimo = 10m,
            FechaInicioUtc = now.AddHours(-1),
            FechaFinUtc = now.Add(remaining ?? TimeSpan.FromMinutes(10)),
            Estado = auctionState
        });

        await context.SaveChangesAsync();
        return database;
    }

    public ApplicationDbContext CreateContext() => new(Options);

    public BidService CreateService(ApplicationDbContext context)
    {
        return new BidService(
            context,
            NoOpHubContext.Instance,
            NullLogger<BidService>.Instance,
            new FixedTimeProvider(Now));
    }

    public async Task SeedLeadingBidAsync(
        Guid bidderId,
        decimal amount)
    {
        await using var context = CreateContext();
        var auction = await context.Subastas.SingleAsync(
            auction => auction.Id == AuctionId);
        var wallet = await context.Billeteras.SingleAsync(
            item => item.UsuarioId == bidderId);

        auction.PrecioActual = amount;
        wallet.SaldoRetenido = amount;
        wallet.SaldoDisponible = wallet.SaldoTotal - amount;

        context.Pujas.Add(new Puja
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            SubastaId = AuctionId,
            UsuarioId = bidderId,
            Monto = amount,
            EsGanadora = true,
            FechaUtc = Now.AddMinutes(-1)
        });
        context.TransaccionLedgers.Add(new TransaccionLedger
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
            BilleteraId = wallet.Id,
            Tipo = TipoMovimientoBilletera.Retencion,
            Monto = amount,
            Descripcion = $"Retención inicial para la subasta {AuctionId}.",
            FechaUtc = Now.AddMinutes(-1)
        });

        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private static Usuario CreateUser(
        Guid id,
        string email,
        string name)
    {
        return new Usuario
        {
            Id = id,
            Email = email,
            Nombre = name,
            PasswordHash = "TEST_ONLY",
            FechaRegistro = new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero)
        };
    }

    private static Billetera CreateWallet(
        Guid id,
        Usuario user,
        decimal balance)
    {
        return new Billetera
        {
            Id = id,
            UsuarioId = user.Id,
            Usuario = user,
            SaldoTotal = balance,
            SaldoRetenido = 0m,
            SaldoDisponible = balance
        };
    }
}
