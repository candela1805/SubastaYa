namespace SubastaYa.API.Services;

public sealed class AuctionOperationConflictException : Exception
{
    public AuctionOperationConflictException(string message)
        : base(message)
    {
    }
}
