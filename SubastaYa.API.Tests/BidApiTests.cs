using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class BidApiTests
{
    public static TheoryData<Guid, Guid, decimal, HttpStatusCode, string>
        RejectedBidCases => new()
        {
            {
                BidTestDatabase.BidderOneId,
                BidTestDatabase.AuctionId,
                109m,
                HttpStatusCode.BadRequest,
                "BID_BELOW_MINIMUM"
            },
            {
                BidTestDatabase.SellerId,
                BidTestDatabase.AuctionId,
                110m,
                HttpStatusCode.Forbidden,
                "OWN_AUCTION_BID"
            },
            {
                BidTestDatabase.BidderOneId,
                Guid.Parse("30000000-0000-0000-0000-000000000099"),
                110m,
                HttpStatusCode.NotFound,
                "AUCTION_NOT_FOUND"
            },
            {
                BidTestDatabase.BidderOneId,
                BidTestDatabase.AuctionId,
                1_100m,
                HttpStatusCode.UnprocessableEntity,
                "INSUFFICIENT_BALANCE"
            }
        };

    [Fact]
    public async Task Post_bid_without_authentication_returns_unauthorized()
    {
        await using var factory = new BidApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/auctions/{BidTestDatabase.AuctionId}/bids",
            new { monto = 110m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_posts_return_one_success_and_one_bid_conflict()
    {
        await using var factory = new BidApiFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync();
        factory.SaveInterceptor.Enabled = true;

        using var firstRequest = CreateBidRequest(
            BidTestDatabase.BidderOneId);
        using var secondRequest = CreateBidRequest(
            BidTestDatabase.BidderTwoId);

        var responses = await Task.WhenAll(
            client.SendAsync(firstRequest),
            client.SendAsync(secondRequest));

        var success = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Conflict);
        var successBody = await success.Content.ReadFromJsonAsync<BidResponse>();
        var conflictBody = await conflict.Content
            .ReadFromJsonAsync<BidErrorResponse>();

        Assert.NotNull(successBody);
        Assert.Equal(110m, successBody.Monto);
        Assert.NotNull(conflictBody);
        Assert.Equal("BID_CONFLICT", conflictBody.Code);
    }

    [Theory]
    [MemberData(nameof(RejectedBidCases))]
    public async Task Post_bid_maps_domain_rejections_to_expected_http_errors(
        Guid userId,
        Guid auctionId,
        decimal amount,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await using var factory = new BidApiFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync();
        using var request = CreateBidRequest(userId, amount, auctionId);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<BidErrorResponse>();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(expectedCode, body.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    [Fact]
    public async Task Get_room_state_requires_authentication()
    {
        await using var factory = new BidApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/auctions/{BidTestDatabase.AuctionId}/bids/state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_room_state_reports_none_leading_and_outbid_for_current_user()
    {
        await using var factory = new BidApiFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync();

        using (var initialRequest = CreateStateRequest(
                   BidTestDatabase.BidderOneId))
        using (var initialResponse = await client.SendAsync(initialRequest))
        {
            Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
            var initial = await initialResponse.Content
                .ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "SinPuja",
                initial.GetProperty("estadoPostor").GetString());
            Assert.Equal(
                110m,
                initial.GetProperty("pujaMinimaSiguiente").GetDecimal());
        }

        using (var firstBid = CreateBidRequest(
                   BidTestDatabase.BidderOneId,
                   110m))
        using (var firstResponse = await client.SendAsync(firstBid))
        {
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using (var leadingRequest = CreateStateRequest(
                   BidTestDatabase.BidderOneId))
        using (var leadingResponse = await client.SendAsync(leadingRequest))
        {
            var leading = await leadingResponse.Content
                .ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "Liderando",
                leading.GetProperty("estadoPostor").GetString());
        }

        using (var secondBid = CreateBidRequest(
                   BidTestDatabase.BidderTwoId,
                   120m))
        using (var secondResponse = await client.SendAsync(secondBid))
        {
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        }

        using (var outbidRequest = CreateStateRequest(
                   BidTestDatabase.BidderOneId))
        using (var outbidResponse = await client.SendAsync(outbidRequest))
        {
            var outbid = await outbidResponse.Content
                .ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "Superado",
                outbid.GetProperty("estadoPostor").GetString());
            Assert.Equal(
                130m,
                outbid.GetProperty("pujaMinimaSiguiente").GetDecimal());
        }

        using var winnerRequest = CreateStateRequest(
            BidTestDatabase.BidderTwoId);
        using var winnerResponse = await client.SendAsync(winnerRequest);
        var winner = await winnerResponse.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            "Liderando",
            winner.GetProperty("estadoPostor").GetString());
    }

    private static HttpRequestMessage CreateBidRequest(
        Guid userId,
        decimal amount = 110m,
        Guid? auctionId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/auctions/{auctionId ?? BidTestDatabase.AuctionId}/bids")
        {
            Content = JsonContent.Create(new { monto = amount })
        };
        request.Headers.Add(
            TestAuthenticationHandler.UserIdHeader,
            userId.ToString());

        return request;
    }

    private static HttpRequestMessage CreateStateRequest(Guid userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/auctions/{BidTestDatabase.AuctionId}/bids/state");
        request.Headers.Add(
            TestAuthenticationHandler.UserIdHeader,
            userId.ToString());

        return request;
    }
}
