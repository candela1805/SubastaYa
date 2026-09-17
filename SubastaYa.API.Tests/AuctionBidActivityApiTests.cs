using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class AuctionBidActivityApiTests
{
    [Fact]
    public async Task My_bids_requires_authentication()
    {
        await using var factory = new BidApiFactory();

        using var response = await factory.CreateClient().GetAsync(
            "/api/auctions/my-bids");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task My_bids_returns_one_activity_per_auction_with_real_amounts()
    {
        await using var factory = new BidApiFactory();
        await SeedActivities(factory);

        using var request = Request(BidTestDatabase.BidderOneId);
        using var response = await factory.CreateClient().SendAsync(request);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, items.GetArrayLength());

        var active = Find(items, BidTestDatabase.AuctionId);
        Assert.Equal("150", active.GetProperty("miOferta").GetString());
        Assert.Equal("170", active.GetProperty("ofertaActual").GetString());
        Assert.Equal("EnCurso", active.GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task My_bids_classifies_won_and_lost_from_settlement()
    {
        await using var factory = new BidApiFactory();
        var ids = await SeedActivities(factory);

        using var request = Request(BidTestDatabase.BidderOneId);
        using var response = await factory.CreateClient().SendAsync(request);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Ganada", Find(items, ids.Won).GetProperty("resultado").GetString());
        Assert.Equal("Perdida", Find(items, ids.Lost).GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task My_bids_is_isolated_to_authenticated_user()
    {
        await using var factory = new BidApiFactory();
        var ids = await SeedActivities(factory);

        using var request = Request(BidTestDatabase.BidderTwoId);
        using var response = await factory.CreateClient().SendAsync(request);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, items.GetArrayLength());
        Assert.DoesNotContain(
            items.EnumerateArray(),
            item => item.GetProperty("subastaId").GetGuid() == ids.Won);
        Assert.Contains(
            items.EnumerateArray(),
            item => item.GetProperty("subastaId").GetGuid() == ids.OtherUserOnly);
    }

    private static async Task<(Guid Won, Guid Lost, Guid OtherUserOnly)>
        SeedActivities(BidApiFactory factory)
    {
        await factory.SeedAsync();
        var wonId = Guid.NewGuid();
        var lostId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Subastas.Single().PrecioActual = 170m;
        db.Subastas.AddRange(
            Auction(wonId, "Ganada", 220m, EstadoSubasta.Finalizada, now),
            Auction(lostId, "Perdida", 330m, EstadoSubasta.Finalizada, now),
            Auction(otherId, "Solo otro", 440m, EstadoSubasta.Activa, now));

        var activeFirst = Bid(BidTestDatabase.AuctionId, BidTestDatabase.BidderOneId, 120m, false, now.AddMinutes(-5));
        var activeMaximum = Bid(BidTestDatabase.AuctionId, BidTestDatabase.BidderOneId, 150m, false, now.AddMinutes(-3));
        var activeLeader = Bid(BidTestDatabase.AuctionId, BidTestDatabase.BidderTwoId, 170m, true, now.AddMinutes(-1));
        var wonBid = Bid(wonId, BidTestDatabase.BidderOneId, 220m, true, now.AddHours(-2));
        var lostOwnBid = Bid(lostId, BidTestDatabase.BidderOneId, 300m, false, now.AddHours(-2));
        var lostWinner = Bid(lostId, BidTestDatabase.BidderTwoId, 330m, true, now.AddHours(-1));
        var otherBid = Bid(otherId, BidTestDatabase.BidderTwoId, 440m, true, now.AddMinutes(-1));
        db.Pujas.AddRange(
            activeFirst,
            activeMaximum,
            activeLeader,
            wonBid,
            lostOwnBid,
            lostWinner,
            otherBid);
        db.LiquidacionesSubasta.AddRange(
            Settlement(wonId, wonBid, BidTestDatabase.BidderOneId, 220m, now),
            Settlement(lostId, lostWinner, BidTestDatabase.BidderTwoId, 330m, now));
        await db.SaveChangesAsync();

        return (wonId, lostId, otherId);
    }

    private static Subasta Auction(
        Guid id,
        string title,
        decimal currentPrice,
        EstadoSubasta state,
        DateTimeOffset now) => new()
    {
        Id = id,
        VendedorId = BidTestDatabase.SellerId,
        Titulo = title,
        Descripcion = "Actividad de prueba.",
        Categoria = "Pruebas",
        PrecioInicial = 100m,
        PrecioActual = currentPrice,
        IncrementoMinimo = 10m,
        FechaInicioUtc = now.AddHours(-3),
        FechaFinUtc = state == EstadoSubasta.Activa
            ? now.AddMinutes(10)
            : now.AddHours(-1),
        Estado = state
    };

    private static Puja Bid(
        Guid auctionId,
        Guid userId,
        decimal amount,
        bool winner,
        DateTimeOffset date) => new()
    {
        Id = Guid.NewGuid(),
        SubastaId = auctionId,
        UsuarioId = userId,
        Monto = amount,
        EsGanadora = winner,
        FechaUtc = date
    };

    private static LiquidacionSubasta Settlement(
        Guid auctionId,
        Puja winningBid,
        Guid buyerId,
        decimal amount,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SubastaId = auctionId,
        PujaGanadoraId = winningBid.Id,
        CompradorId = buyerId,
        VendedorId = BidTestDatabase.SellerId,
        ImporteFinal = amount,
        FechaAdjudicacionUtc = now.AddHours(-1),
        FechaLiquidacionUtc = now.AddHours(-1)
    };

    private static HttpRequestMessage Request(Guid userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/auctions/my-bids");
        request.Headers.Add(
            TestAuthenticationHandler.UserIdHeader,
            userId.ToString());
        return request;
    }

    private static JsonElement Find(JsonElement items, Guid auctionId)
    {
        return items.EnumerateArray().Single(item =>
            item.GetProperty("subastaId").GetGuid() == auctionId);
    }
}
