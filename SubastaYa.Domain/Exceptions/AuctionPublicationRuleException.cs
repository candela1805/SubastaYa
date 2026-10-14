namespace SubastaYa.Domain.Exceptions;

public sealed class AuctionPublicationRuleException : Exception
{
    public AuctionPublicationRuleException(string message)
        : base(message)
    {
    }
}