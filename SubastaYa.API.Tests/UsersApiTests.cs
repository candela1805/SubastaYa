using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubastaYa.API.Data;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class UsersApiTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Profile_requires_authentication(string method)
    {
        await using var factory = new BidApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            "/api/users/me");
        if (method == "PUT")
            request.Content = JsonContent.Create(new { nombre = "Test", email = "test@example.com" });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_profile_returns_only_safe_authenticated_user_data()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await Send(
            factory,
            HttpMethod.Get,
            BidTestDatabase.BidderOneId);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BidTestDatabase.BidderOneId, json.GetProperty("userId").GetGuid());
        Assert.Equal("bidder-one-api@tests.local", json.GetProperty("email").GetString());
        Assert.False(json.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Owner_updates_name_and_email_without_changing_internal_data()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        string originalPassword;
        Guid walletId;
        decimal walletTotal;

        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var db = beforeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Usuarios.SingleAsync(item => item.Id == BidTestDatabase.BidderOneId);
            var wallet = await db.Billeteras.SingleAsync(item => item.UsuarioId == user.Id);
            originalPassword = user.PasswordHash;
            walletId = wallet.Id;
            walletTotal = wallet.SaldoTotal;
        }

        using var response = await SendJson(
            factory,
            BidTestDatabase.BidderOneId,
            new
            {
                nombre = "  Nombre Actualizado  ",
                email = "  NUEVO@EXAMPLE.COM  ",
                userId = BidTestDatabase.BidderTwoId,
                passwordHash = "NO_DEBE_CAMBIAR"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await context.Usuarios.SingleAsync(
            item => item.Id == BidTestDatabase.BidderOneId);
        var other = await context.Usuarios.SingleAsync(
            item => item.Id == BidTestDatabase.BidderTwoId);
        var walletAfter = await context.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);

        Assert.Equal("Nombre Actualizado", updated.Nombre);
        Assert.Equal("nuevo@example.com", updated.Email);
        Assert.Equal(BidTestDatabase.BidderOneId, updated.Id);
        Assert.Equal(originalPassword, updated.PasswordHash);
        Assert.Equal(BidTestDatabase.BidderTwoId, other.Id);
        Assert.Equal("bidder-two-api@tests.local", other.Email);
        Assert.Equal(walletId, walletAfter.Id);
        Assert.Equal(walletTotal, walletAfter.SaldoTotal);
    }

    [Fact]
    public async Task Invalid_email_is_rejected()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await SendJson(
            factory,
            BidTestDatabase.BidderOneId,
            new { nombre = "Nombre", email = "email-invalido" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_without_modifying_user()
    {
        await using var factory = new BidApiFactory();
        await factory.SeedAsync();
        using var response = await SendJson(
            factory,
            BidTestDatabase.BidderOneId,
            new
            {
                nombre = "Nombre",
                email = "bidder-two-api@tests.local"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Usuarios.SingleAsync(
            item => item.Id == BidTestDatabase.BidderOneId);
        Assert.Equal("bidder-one-api@tests.local", user.Email);
    }

    private static async Task<HttpResponseMessage> Send(
        BidApiFactory factory,
        HttpMethod method,
        Guid userId)
    {
        using var request = new HttpRequestMessage(method, "/api/users/me");
        request.Headers.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendJson<T>(
        BidApiFactory factory,
        Guid userId,
        T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me");
        request.Headers.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        request.Content = JsonContent.Create(body);
        return await factory.CreateClient().SendAsync(request);
    }
}
