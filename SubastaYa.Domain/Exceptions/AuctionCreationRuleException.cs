namespace SubastaYa.Domain.Exceptions;

public sealed class AuctionCreationRuleException : Exception
{
    public AuctionCreationRuleException(string message)
        : base(message)
    {
    }
}