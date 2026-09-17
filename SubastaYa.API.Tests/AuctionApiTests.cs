using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubastaYa.API.Contracts.Auctions;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class AuctionApiTests
{
    private static readonly DateTimeOffset NewEnd =
        new(2026, 9, 14, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_mine_requires_authentication()
    {
        await using var factory = new BidApiFactory();
        using var response = await factory.CreateClient().GetAsync("/api/auctions/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_mine_returns_only_owner_items()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        using var owner = Request(HttpMethod.Get, "/api/auctions/mine", BidTestDatabase.SellerId);
        using var other = Request(HttpMethod.Get, "/api/auctions/mine", BidTestDatabase.BidderOneId);
        using var ownerResponse = await client.SendAsync(owner);
        using var otherResponse = await client.SendAsync(other);
        var ownerItems = await ownerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var otherItems = await otherResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(
            BidTestDatabase.AuctionId,
            Assert.Single(ownerItems.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty(otherItems.EnumerateArray());
    }

    [Fact]
    public async Task Categories_are_public_trimmed_unique_and_ordered()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await Mutate(factory, db =>
        {
            db.Subastas.Single().Categoria = " tecnología ";
            db.Subastas.AddRange(
                AuctionWithCategory("Tecnología"),
                AuctionWithCategory("Tecnología"),
                AuctionWithCategory("Alpha"),
                AuctionWithCategory("   "));
        });

        using var response = await factory.CreateClient().GetAsync(
            "/api/auctions/categories");
        var categories = await response.Content.ReadFromJsonAsync<List<string>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Alpha", "Tecnología" }, categories);
    }

    [Fact]
    public async Task Update_requires_authentication()
    {
        await using var factory = new BidApiFactory();
        using var response = await factory.CreateClient().PutAsJsonAsync(Url, Update());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_updates_only_allowed_fields_and_creates_audit()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await SendJson(factory, HttpMethod.Put, Url,
            BidTestDatabase.SellerId, Update());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auction = await db.Subastas.SingleAsync();
        Assert.Equal("Título actualizado", auction.Titulo);
        Assert.Equal("Descripción actualizada", auction.Descripcion);
        Assert.Equal("Categoría actualizada", auction.Categoria);
        Assert.Equal("https://example.test/image.jpg", auction.ImagenUrl);
        Assert.Equal(NewEnd, auction.FechaFinUtc);
        Assert.Equal(100m, auction.PrecioInicial);
        Assert.Equal(100m, auction.PrecioActual);
        Assert.Equal(10m, auction.IncrementoMinimo);
        Assert.Equal(BidTestDatabase.SellerId, auction.VendedorId);
        Assert.Equal(EstadoSubasta.Activa, auction.Estado);
        Assert.Contains(await db.AuditoriaLogs.ToListAsync(),
            item => item.TipoEvento == "AuctionUpdated");
    }

    [Fact]
    public async Task Other_user_cannot_update()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await SendJson(factory, HttpMethod.Put, Url,
            BidTestDatabase.BidderOneId, Update());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Subasta API concurrente", await Title(factory));
    }

    [Fact]
    public async Task Missing_auction_cannot_be_updated()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await SendJson(factory, HttpMethod.Put,
            $"/api/auctions/{Guid.NewGuid()}", BidTestDatabase.SellerId, Update());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Update_contract_exposes_no_immutable_or_ownership_fields()
    {
        var properties = typeof(UpdateAuctionRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(new[] { "Categoria", "Descripcion", "FechaFinUtc", "ImagenUrl", "Titulo" }, properties);
        Assert.DoesNotContain("PrecioInicial", properties);
        Assert.DoesNotContain("IncrementoMinimo", properties);
        Assert.DoesNotContain("PrecioActual", properties);
        Assert.DoesNotContain("VendedorId", properties);
        Assert.DoesNotContain("Estado", properties);
    }

    [Theory]
    [InlineData(EstadoSubasta.Finalizada)]
    [InlineData(EstadoSubasta.Desierta)]
    public async Task Closed_auction_cannot_be_updated(EstadoSubasta state)
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await SetState(factory, state);
        using var response = await SendJson(factory, HttpMethod.Put, Url,
            BidTestDatabase.SellerId, Update());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Subasta API concurrente", await Title(factory));
    }

    [Fact]
    public async Task Auction_with_bid_cannot_be_updated()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await AddBid(factory);
        using var response = await SendJson(factory, HttpMethod.Put, Url,
            BidTestDatabase.SellerId, Update());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Subasta API concurrente", await Title(factory));
    }

    [Fact]
    public async Task Delete_requires_authentication()
    {
        await using var factory = new BidApiFactory();
        using var response = await factory.CreateClient().DeleteAsync(Url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_deletes_history_free_auction()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await Exists(factory));
    }

    [Fact]
    public async Task Owner_deletes_scheduled_auction_without_bids()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await SetState(factory, EstadoSubasta.Programada);
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await Exists(factory));
    }

    [Fact]
    public async Task Other_user_cannot_delete()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.BidderOneId);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await Exists(factory));
    }

    [Fact]
    public async Task Missing_auction_cannot_be_deleted()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await Send(factory, HttpMethod.Delete,
            $"/api/auctions/{Guid.NewGuid()}", BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(EstadoSubasta.Finalizada)]
    [InlineData(EstadoSubasta.Desierta)]
    public async Task Closed_auction_without_bids_can_be_deleted(EstadoSubasta state)
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await SetState(factory, state);
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await Exists(factory));
    }

    [Fact]
    public async Task Bid_history_blocks_delete_and_is_preserved()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await AddBid(factory);
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Esta publicación no puede eliminarse porque ya recibió una puja.",
            error.GetProperty("message").GetString());
        await AssertCounts(factory, bids: 1, audits: 0, settlements: 0);
    }

    [Fact]
    public async Task Technical_audit_does_not_block_delete_without_bids()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await Mutate(factory, db => db.AuditoriaLogs.Add(new AuditoriaLog
        {
            Id = Guid.NewGuid(), SubastaId = BidTestDatabase.AuctionId,
            TipoEvento = "Test", Detalle = "Debe conservarse.", FechaUtc = NewEnd.AddHours(-2)
        }));
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Subastas.AnyAsync());
        Assert.Empty(await db.AuditoriaLogs.ToListAsync());
    }

    [Fact]
    public async Task Settlement_without_bid_blocks_delete_and_is_preserved()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        await Mutate(factory, db =>
        {
            db.LiquidacionesSubasta.Add(new LiquidacionSubasta
            {
                Id = Guid.NewGuid(), SubastaId = BidTestDatabase.AuctionId,
                PujaGanadoraId = Guid.NewGuid(), CompradorId = BidTestDatabase.BidderOneId,
                VendedorId = BidTestDatabase.SellerId, ImporteFinal = 110m,
                FechaAdjudicacionUtc = NewEnd.AddHours(-1), FechaLiquidacionUtc = NewEnd
            });
        });
        using var response = await Send(factory, HttpMethod.Delete, Url, BidTestDatabase.SellerId);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCounts(factory, bids: 0, audits: 0, settlements: 1);
    }

    private static string Url => $"/api/auctions/{BidTestDatabase.AuctionId}";
    private static UpdateAuctionRequest Update() => new()
    {
        Titulo = "Título actualizado", Descripcion = "Descripción actualizada",
        Categoria = "Categoría actualizada", ImagenUrl = "https://example.test/image.jpg",
        FechaFinUtc = NewEnd
    };
    private static Puja NewBid() => new()
    {
        Id = Guid.NewGuid(), SubastaId = BidTestDatabase.AuctionId,
        UsuarioId = BidTestDatabase.BidderOneId, Monto = 110m,
        EsGanadora = true, FechaUtc = NewEnd.AddHours(-2)
    };
    private static Subasta AuctionWithCategory(string category) => new()
    {
        Id = Guid.NewGuid(),
        VendedorId = BidTestDatabase.SellerId,
        Titulo = $"Subasta {Guid.NewGuid():N}",
        Descripcion = "Prueba de categorías.",
        Categoria = category,
        PrecioInicial = 100m,
        PrecioActual = 100m,
        IncrementoMinimo = 10m,
        FechaInicioUtc = NewEnd.AddHours(-2),
        FechaFinUtc = NewEnd,
        Estado = EstadoSubasta.Activa
    };
    private static HttpRequestMessage Request(HttpMethod method, string url, Guid userId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return request;
    }
    private static async Task<HttpResponseMessage> Send(
        BidApiFactory factory, HttpMethod method, string url, Guid userId)
    {
        using var request = Request(method, url, userId);
        return await factory.CreateClient().SendAsync(request);
    }
    private static async Task<HttpResponseMessage> SendJson<T>(
        BidApiFactory factory, HttpMethod method, string url, Guid userId, T body)
    {
        using var request = Request(method, url, userId);
        request.Content = JsonContent.Create(body);
        return await factory.CreateClient().SendAsync(request);
    }
    private static async Task Mutate(BidApiFactory factory, Action<ApplicationDbContext> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        action(db);
        await db.SaveChangesAsync();
    }
    private static Task AddBid(BidApiFactory factory) => Mutate(factory, db => db.Pujas.Add(NewBid()));
    private static Task SetState(BidApiFactory factory, EstadoSubasta state) => Mutate(factory, db =>
    {
        db.Subastas.Single().Estado = state;
    });
    private static async Task<string> Title(BidApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Subastas.Select(item => item.Titulo).SingleAsync();
    }
    private static async Task<bool> Exists(BidApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Subastas.AnyAsync();
    }
    private static async Task AssertCounts(
        BidApiFactory factory, int bids, int audits, int settlements)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Subastas.AnyAsync());
        Assert.Equal(bids, await db.Pujas.CountAsync());
        Assert.Equal(audits, await db.AuditoriaLogs.CountAsync());
        Assert.Equal(settlements, await db.LiquidacionesSubasta.CountAsync());
    }
}
