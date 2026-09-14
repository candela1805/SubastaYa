namespace SubastaYa.API.Contracts.Bids;

public sealed class BidErrorResponse
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
