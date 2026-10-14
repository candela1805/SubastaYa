
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SubastaYa.Domain.Models;
using Microsoft.Extensions.Options;

namespace SubastaYa.Infrastructure.Data;

public sealed class DevelopmentDataSeeder
{
    public static readonly Guid DemoUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SellerId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    public static readonly Guid BuyerOneId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    public static readonly Guid BuyerTwoId = Guid.Parse("50000000-0000-0000-0000-000000000003");
    public static readonly Guid NoFundsId = Guid.Parse("50000000-0000-0000-0000-000000000004");

    public static readonly Guid StandardAuctionId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    public static readonly Guid CriticalAuctionId = Guid.Parse("60000000-0000-0000-0000-000000000002");
    public static readonly Guid ScheduledAuctionId = Guid.Parse("60000000-0000-0000-0000-000000000003");
    public static readonly Guid ExpiredWinnerAuctionId = Guid.Parse("60000000-0000-0000-0000-000000000004");
    public static readonly Guid ExpiredDesertedAuctionId = Guid.Parse("60000000-0000-0000-0000-000000000005");

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly DevelopmentSeedOptions _options;

    public DevelopmentDataSeeder(
        ApplicationDbContext db,
        IPasswordHasher<Usuario> passwordHasher,
        TimeProvider timeProvider,
        IOptions<DevelopmentSeedOptions> options)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task SeedAsync(
        string demoName,
        string demoEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException(
                "Configurá DevelopmentSeed:Password mediante User Secrets antes de ejecutar el seed.");
        }
        var now = _timeProvider.GetUtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            cancellationToken);

        var demo = await EnsureUserAsync(
            DemoUserId, demoName, demoEmail, 10_000m, 10_000m, now,
            cancellationToken);
        var seller = await EnsureUserAsync(
            SellerId, "Vendedor Seed", "vendedor@test.com", 0m, 0m, now,
            cancellationToken);
        var buyerOne = await EnsureUserAsync(
            BuyerOneId, "Comprador Uno", "comprador1@test.com",
            150_000m, 45_000m, now, cancellationToken);
        var buyerTwo = await EnsureUserAsync(
            BuyerTwoId, "Comprador Dos", "comprador2@test.com",
            200_000m, 0m, now, cancellationToken);
        await EnsureUserAsync(
            NoFundsId, "Usuario Sin Fondos", "sinfondos@test.com",
            500m, 0m, now, cancellationToken);

        if (!await _db.Subastas.AnyAsync(
                item => item.Id == StandardAuctionId, cancellationToken))
        {
            AddStandardAuction(seller, buyerOne, buyerTwo, now);
        }

        AddAuctionIfMissing(
            CriticalAuctionId, seller.Id, "Notebook para anti-sniping",
            "TecnologÃ­a", 70_000m, 5_000m, now.AddHours(-1),
            now.AddSeconds(90), EstadoSubasta.Activa);
        AddAuctionIfMissing(
            ScheduledAuctionId, seller.Id, "ColecciÃ³n de monedas",
            "Coleccionables", 20_000m, 2_000m, now.AddHours(24),
            now.AddHours(26), EstadoSubasta.Programada);

        if (!await _db.Subastas.AnyAsync(
                item => item.Id == ExpiredWinnerAuctionId, cancellationToken))
        {
            EnsureDemoFundsForExpiredAuction(demo, now);
            AddExpiredWinnerAuction(seller, demo, now);
        }

        AddAuctionIfMissing(
            ExpiredDesertedAuctionId, seller.Id, "Campera sin ofertas",
            "Indumentaria", 30_000m, 3_000m, now.AddHours(-3),
            now.AddMinutes(-2), EstadoSubasta.Activa);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Usuario> EnsureUserAsync(
        Guid id,
        string name,
        string email,
        decimal total,
        decimal retained,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await _db.Usuarios
            .Include(item => item.Billetera)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (user is not null)
        {
            if (user.Billetera is null)
            {
                user.Billetera = Wallet(id, total, retained);
                AddInitialDeposit(user, total, now);
            }

            return user;
        }

        user = new Usuario
        {
            Id = id,
            Nombre = name,
            Email = email,
            FechaRegistro = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(
    user,
    _options.Password);
        user.Billetera = Wallet(id, total, retained);
        _db.Usuarios.Add(user);

        AddInitialDeposit(user, total, now);

        return user;
    }

    private void AddInitialDeposit(
        Usuario user,
        decimal total,
        DateTimeOffset now)
    {
        if (total <= 0)
        {
            return;
        }

        _db.TransaccionLedgers.Add(Ledger(
            user.Billetera!.Id,
            $"seed:deposit:{user.Id}",
            TipoMovimientoBilletera.Deposito,
            total,
            "DepÃ³sito inicial de datos de demostraciÃ³n.",
            now.AddMinutes(-10)));
    }

    private void EnsureDemoFundsForExpiredAuction(
        Usuario demo,
        DateTimeOffset now)
    {
        const string depositKey = "seed:deposit:11111111-1111-1111-1111-111111111111";
        var depositExists = _db.TransaccionLedgers.Local.Any(item =>
                item.ClaveIdempotencia == depositKey) ||
            _db.TransaccionLedgers.AsNoTracking().Any(item =>
                item.ClaveIdempotencia == depositKey);

        if (depositExists)
        {
            return;
        }

        demo.Billetera!.SaldoTotal += 10_000m;
        demo.Billetera.SaldoRetenido += 10_000m;
        _db.TransaccionLedgers.Add(Ledger(
            demo.Billetera.Id,
            depositKey,
            TipoMovimientoBilletera.Deposito,
            10_000m,
            "DepÃ³sito inicial para liquidaciÃ³n seed.",
            now.AddMinutes(-10)));
    }

    private void AddStandardAuction(
        Usuario seller,
        Usuario buyerOne,
        Usuario buyerTwo,
        DateTimeOffset now)
    {
        var firstBidId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var winningBidId = Guid.Parse("70000000-0000-0000-0000-000000000002");
        _db.Subastas.Add(Auction(
            StandardAuctionId, seller.Id, "Smartphone de demostraciÃ³n",
            "TecnologÃ­a", 35_000m, 45_000m, 5_000m,
            now.AddHours(-1), now.AddMinutes(25), EstadoSubasta.Activa));
        _db.Pujas.AddRange(
            Bid(firstBidId, StandardAuctionId, buyerTwo.Id, 40_000m, false, now.AddMinutes(-20)),
            Bid(winningBidId, StandardAuctionId, buyerOne.Id, 45_000m, true, now.AddMinutes(-15)));
        _db.TransaccionLedgers.AddRange(
            Ledger(buyerTwo.Billetera!.Id, "seed:standard:buyer2:hold",
                TipoMovimientoBilletera.Retencion, 40_000m,
                "RetenciÃ³n por primera puja seed.", now.AddMinutes(-20)),
            Ledger(buyerTwo.Billetera.Id, "seed:standard:buyer2:release",
                TipoMovimientoBilletera.Liberacion, 40_000m,
                "LiberaciÃ³n al ser superada la primera puja seed.", now.AddMinutes(-15)),
            Ledger(buyerOne.Billetera!.Id, "seed:standard:buyer1:hold",
                TipoMovimientoBilletera.Retencion, 45_000m,
                "RetenciÃ³n por puja lÃ­der seed.", now.AddMinutes(-15)));
        AddBidAudit(StandardAuctionId, firstBidId, buyerTwo.Id, 40_000m, now.AddMinutes(-20));
        AddBidAudit(StandardAuctionId, winningBidId, buyerOne.Id, 45_000m, now.AddMinutes(-15));
    }

    private void AddExpiredWinnerAuction(
        Usuario seller,
        Usuario demo,
        DateTimeOffset now)
    {
        var bidId = Guid.Parse("70000000-0000-0000-0000-000000000003");
        _db.Subastas.Add(Auction(
            ExpiredWinnerAuctionId, seller.Id, "Motocicleta para liquidar",
            "VehÃ­culos", 8_000m, 10_000m, 2_000m,
            now.AddHours(-3), now.AddMinutes(-2), EstadoSubasta.Activa));
        _db.Pujas.Add(Bid(
            bidId, ExpiredWinnerAuctionId, demo.Id, 10_000m, true,
            now.AddMinutes(-10)));
        _db.TransaccionLedgers.Add(Ledger(
            demo.Billetera!.Id, "seed:expired:demo:hold",
            TipoMovimientoBilletera.Retencion, 10_000m,
            "RetenciÃ³n para la subasta vencida seed.", now.AddMinutes(-10)));
        AddBidAudit(
            ExpiredWinnerAuctionId, bidId, demo.Id, 10_000m,
            now.AddMinutes(-10));
    }

    private void AddAuctionIfMissing(
        Guid id, Guid sellerId, string title, string category,
        decimal initialPrice, decimal increment, DateTimeOffset start,
        DateTimeOffset end, EstadoSubasta state)
    {
        if (_db.Subastas.Local.Any(item => item.Id == id) ||
            _db.Subastas.AsNoTracking().Any(item => item.Id == id))
        {
            return;
        }

        _db.Subastas.Add(Auction(
            id, sellerId, title, category, initialPrice, initialPrice,
            increment, start, end, state));
    }

    private void AddBidAudit(
        Guid auctionId, Guid bidId, Guid userId, decimal amount,
        DateTimeOffset date)
    {
        _db.AuditoriaLogs.Add(new AuditoriaLog
        {
            Id = DeterministicAuditId(bidId),
            SubastaId = auctionId,
            TipoEvento = "BidPlaced",
            Detalle = $"Puja seed {bidId} confirmada por {amount:F2} para el usuario {userId}.",
            ClaveIdempotencia = $"seed:bid:{bidId}",
            FechaUtc = date
        });
    }

    private static Billetera Wallet(Guid userId, decimal total, decimal retained) => new()
    {
        Id = DeterministicWalletId(userId),
        UsuarioId = userId,
        SaldoTotal = total,
        SaldoRetenido = retained,
        SaldoDisponible = total - retained
    };

    private static Subasta Auction(
        Guid id, Guid sellerId, string title, string category,
        decimal initialPrice, decimal currentPrice, decimal increment,
        DateTimeOffset start, DateTimeOffset end, EstadoSubasta state) => new()
    {
        Id = id,
        VendedorId = sellerId,
        Titulo = title,
        Descripcion = "Escenario acadÃ©mico generado automÃ¡ticamente en Development.",
        Categoria = category,
        PrecioInicial = initialPrice,
        PrecioActual = currentPrice,
        IncrementoMinimo = increment,
        FechaInicioUtc = start,
        FechaFinUtc = end,
        Estado = state
    };

    private static Puja Bid(
        Guid id, Guid auctionId, Guid userId, decimal amount,
        bool winner, DateTimeOffset date) => new()
    {
        Id = id,
        SubastaId = auctionId,
        UsuarioId = userId,
        Monto = amount,
        EsGanadora = winner,
        FechaUtc = date
    };

    private static TransaccionLedger Ledger(
        Guid walletId, string key, TipoMovimientoBilletera type,
        decimal amount, string description, DateTimeOffset date) => new()
    {
        Id = DeterministicLedgerId(key),
        BilleteraId = walletId,
        ClaveIdempotencia = key,
        Tipo = type,
        Monto = amount,
        Descripcion = description,
        FechaUtc = date
    };

    private static Guid DeterministicWalletId(Guid userId) =>
        Guid.Parse($"51000000-0000-0000-0000-{userId.ToString("N")[20..]}");

    private static Guid DeterministicAuditId(Guid bidId) =>
        Guid.Parse($"72000000-0000-0000-0000-{bidId.ToString("N")[20..]}");

    private static Guid DeterministicLedgerId(string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(bytes[..16]);
    }
}
