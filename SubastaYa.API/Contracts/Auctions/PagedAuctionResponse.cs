namespace SubastaYa.API.Contracts.Auctions;

public sealed class PagedAuctionResponse
{
    public IReadOnlyList<AuctionResponse> Items { get; set; } = [];

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }
}
