using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

namespace SubastaYa.API.Tests.TestInfrastructure;

internal sealed class AuctionClosingTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private AuctionClosingTestDatabase(
        SqliteConnection connection,
        DbContextOptions<ApplicationDbContext> options,
        DateTimeOffset now)
    {
        _connection = connection;
        Options = options;
        Clock = new FixedTimeProvider(now);
    }

    public static readonly Guid SellerId =
        Guid.Parse("71000000-0000-0000-0000-000000000001");

    public static readonly Guid BuyerId =
        Guid.Parse("72000000-0000-0000-0000-000000000001");

    public static readonly Guid OtherBidderId =
        Guid.Parse("72000000-0000-0000-0000-000000000002");

    public static readonly Guid AuctionId =
        Guid.Parse("73000000-0000-0000-0000-000000000001");

    public static readonly Guid SellerWalletId =
        Guid.Parse("74000000-0000-0000-0000-000000000001");

    public static readonly Guid BuyerWalletId =
        Guid.Parse("74000000-0000-0000-0000-000000000002");

    public static readonly Guid OtherBidderWalletId =
        Guid.Parse("74000000-0000-0000-0000-000000000003");

    public static readonly Guid WinningBidId =
        Guid.Parse("75000000-0000-0000-0000-000000000001");

    public static readonly Guid LosingBidId =
        Guid.Parse("75000000-0000-0000-0000-000000000002");

    public DbContextOptions<ApplicationDbContext> Options { get; }

    public FixedTimeProvider Clock { get; }

    public DateTimeOffset Now => Clock.GetUtcNow();

    public static async Task<AuctionClosingTestDatabase> CreateAsync(
        TimeSpan? remaining = null)
    {
        var now = new DateTimeOffset(
            2026,
            9,
            15,
            21,
            0,
            0,
            TimeSpan.Zero);
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;
        var database = new AuctionClosingTestDatabase(
            connection,
            options,
            now);

        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();

        var seller = CreateUser(
            SellerId,
            "seller-closing@tests.local",
            "Vendedor");
        var buyer = CreateUser(
            BuyerId,
            "buyer-closing@tests.local",
            "Comprador");
        var otherBidder = CreateUser(
            OtherBidderId,
            "other-bidder-closing@tests.local",
            "Otro postor");

        context.Usuarios.AddRange(seller, buyer, otherBidder);
        context.Billeteras.AddRange(
            CreateWallet(
                SellerWalletId,
                seller,
                total: 500m,
                retained: 100m),
            CreateWallet(
                BuyerWalletId,
                buyer,
                total: 1_000m,
                retained: 0m),
            CreateWallet(
                OtherBidderWalletId,
                otherBidder,
                total: 1_000m,
                retained: 0m));
        context.Subastas.Add(new Subasta
        {
            Id = AuctionId,
            VendedorId = SellerId,
            Titulo = "Subasta vencida para liquidación",
            Descripcion = "Subasta usada por las pruebas del cierre automático.",
            Categoria = "Tecnología",
            PrecioInicial = 100m,
            PrecioActual = 100m,
            IncrementoMinimo = 10m,
            FechaInicioUtc = now.AddHours(-1),
            FechaFinUtc = now.Add(remaining ?? TimeSpan.FromMinutes(-1)),
            Estado = EstadoSubasta.Activa
        });

        await context.SaveChangesAsync();
        return database;
    }

    public ApplicationDbContext CreateContext() => new(Options);

    public AuctionClosingService CreateService(
        ApplicationDbContext context)
    {
        return new AuctionClosingService(
            context,
            NoOpHubContext.Instance,
            Clock,
            NullLogger<AuctionClosingService>.Instance);
    }

    public async Task SeedWinningBidAsync(
        decimal winningAmount = 130m,
        decimal unrelatedRetention = 50m,
        bool includeLosingBid = true)
    {
        await using var context = CreateContext();
        var auction = await context.Subastas.SingleAsync(
            item => item.Id == AuctionId);
        var buyerWallet = await context.Billeteras.SingleAsync(
            item => item.Id == BuyerWalletId);

        auction.PrecioActual = winningAmount;
        buyerWallet.SaldoRetenido = winningAmount + unrelatedRetention;
        buyerWallet.SaldoDisponible =
            buyerWallet.SaldoTotal - buyerWallet.SaldoRetenido;

        if (includeLosingBid)
        {
            context.Pujas.Add(new Puja
            {
                Id = LosingBidId,
                SubastaId = AuctionId,
                UsuarioId = OtherBidderId,
                Monto = winningAmount - 10m,
                EsGanadora = false,
                FechaUtc = Now.AddMinutes(-2)
            });
        }

        context.Pujas.Add(new Puja
        {
            Id = WinningBidId,
            SubastaId = AuctionId,
            UsuarioId = BuyerId,
            Monto = winningAmount,
            EsGanadora = true,
            FechaUtc = Now.AddMinutes(-1)
        });
        context.TransaccionLedgers.Add(new TransaccionLedger
        {
            Id = Guid.Parse("76000000-0000-0000-0000-000000000001"),
            BilleteraId = BuyerWalletId,
            Tipo = TipoMovimientoBilletera.Retencion,
            Monto = winningAmount,
            Descripcion = "Retención de la puja ganadora de prueba.",
            FechaUtc = Now.AddMinutes(-1)
        });

        if (unrelatedRetention > 0m)
        {
            context.TransaccionLedgers.Add(new TransaccionLedger
            {
                Id = Guid.Parse("76000000-0000-0000-0000-000000000002"),
                BilleteraId = BuyerWalletId,
                Tipo = TipoMovimientoBilletera.Retencion,
                Monto = unrelatedRetention,
                Descripcion = "Retención ajena a la subasta liquidada.",
                FechaUtc = Now.AddMinutes(-3)
            });
        }

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
        decimal total,
        decimal retained)
    {
        return new Billetera
        {
            Id = id,
            UsuarioId = user.Id,
            Usuario = user,
            SaldoTotal = total,
            SaldoRetenido = retained,
            SaldoDisponible = total - retained
        };
    }
}
