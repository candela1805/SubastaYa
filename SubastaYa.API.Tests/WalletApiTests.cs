using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubastaYa.API.Contracts.Wallet;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class WalletApiTests
{
    [Fact]
    public async Task Valid_deposit_updates_balances_and_registers_movement()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();

        using var response = await SendJson(
            factory,
            HttpMethod.Post,
            "/api/wallet/deposit",
            BidTestDatabase.BidderOneId,
            new { monto = 250m });
        var balance = await response.Content.ReadFromJsonAsync<WalletBalanceResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1_250m, balance!.Total);
        Assert.Equal(1_250m, balance.Disponible);
        Assert.Equal(0m, balance.Retenido);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var movement = await db.TransaccionLedgers.SingleAsync();
        Assert.Equal(TipoMovimientoBilletera.Deposito, movement.Tipo);
        Assert.Equal(250m, movement.Monto);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Invalid_deposit_is_rejected_without_changing_balance(decimal amount)
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();

        using var response = await SendJson(
            factory,
            HttpMethod.Post,
            "/api/wallet/deposit",
            BidTestDatabase.BidderOneId,
            new { monto = amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var wallet = await db.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);
        Assert.Equal(1_000m, wallet.SaldoTotal);
        Assert.Empty(await db.TransaccionLedgers.ToListAsync());
    }

    [Fact]
    public async Task Transactions_are_recent_first_and_isolated_by_authenticated_user()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await AddMovement(factory, BidTestDatabase.BidderOneId, 10m, "Propio antiguo", -2);
        await AddMovement(factory, BidTestDatabase.BidderOneId, 20m, "Propio reciente", -1);
        await AddMovement(factory, BidTestDatabase.BidderTwoId, 99m, "Movimiento ajeno", 0);

        using var response = await Send(
            factory,
            HttpMethod.Get,
            "/api/wallet/transactions",
            BidTestDatabase.BidderOneId);
        var movements = await response.Content
            .ReadFromJsonAsync<List<WalletTransactionResponse>>();
        var actualMovements = Assert.IsType<List<WalletTransactionResponse>>(
            movements);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Propio reciente", "Propio antiguo" },
            actualMovements.Select(item => item.Descripcion));
        Assert.DoesNotContain(actualMovements, item => item.Monto == 99m);
    }

    [Fact]
    public async Task Retained_funds_are_derived_from_authenticated_users_open_winning_bids()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await AddWinningBid(factory);

        using var response = await Send(
            factory,
            HttpMethod.Get,
            "/api/wallet/retained-funds",
            BidTestDatabase.BidderOneId);
        var retained = await response.Content
            .ReadFromJsonAsync<RetainedFundsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(150m, retained!.TotalRetenido);
        Assert.Equal(150m, retained.TotalIdentificado);
        Assert.Equal(0m, retained.Diferencia);
        var item = Assert.Single(retained.Items);
        Assert.Equal(BidTestDatabase.AuctionId, item.SubastaId);
        Assert.Equal(150m, item.Monto);

        using var otherResponse = await Send(
            factory,
            HttpMethod.Get,
            "/api/wallet/retained-funds",
            BidTestDatabase.BidderTwoId);
        var otherRetained = await otherResponse.Content
            .ReadFromJsonAsync<RetainedFundsResponse>();
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        Assert.Equal(0m, otherRetained!.TotalRetenido);
        Assert.Empty(otherRetained.Items);
    }

    [Theory]
    [InlineData("/api/wallet/balance")]
    [InlineData("/api/wallet/transactions")]
    [InlineData("/api/wallet/retained-funds")]
    public async Task Private_wallet_queries_require_authentication(string url)
    {
        await using var factory = new BidApiFactory();
        using var response = await factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AddMovement(
        BidApiFactory factory,
        Guid userId,
        decimal amount,
        string description,
        int minutes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var walletId = await db.Billeteras
            .Where(item => item.UsuarioId == userId)
            .Select(item => item.Id)
            .SingleAsync();
        db.TransaccionLedgers.Add(new TransaccionLedger
        {
            Id = Guid.NewGuid(),
            BilleteraId = walletId,
            Tipo = TipoMovimientoBilletera.Deposito,
            Monto = amount,
            Descripcion = description,
            FechaUtc = new DateTimeOffset(
                2026, 9, 14, 18, 0, 0, TimeSpan.Zero).AddMinutes(minutes)
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddWinningBid(BidApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var wallet = await db.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);
        wallet.SaldoRetenido = 150m;
        wallet.SaldoDisponible = wallet.SaldoTotal - wallet.SaldoRetenido;
        db.Pujas.Add(new Puja
        {
            Id = Guid.NewGuid(),
            SubastaId = BidTestDatabase.AuctionId,
            UsuarioId = BidTestDatabase.BidderOneId,
            Monto = 150m,
            EsGanadora = true,
            FechaUtc = new DateTimeOffset(2026, 9, 14, 17, 55, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, Guid userId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return request;
    }

    private static async Task<HttpResponseMessage> Send(
        BidApiFactory factory,
        HttpMethod method,
        string url,
        Guid userId)
    {
        using var request = Request(method, url, userId);
        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendJson<T>(
        BidApiFactory factory,
        HttpMethod method,
        string url,
        Guid userId,
        T body)
    {
        using var request = Request(method, url, userId);
        request.Content = JsonContent.Create(body);
        return await factory.CreateClient().SendAsync(request);
    }
}
