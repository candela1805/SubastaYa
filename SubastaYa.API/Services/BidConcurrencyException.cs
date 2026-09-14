namespace SubastaYa.API.Services;

public sealed class BidConcurrencyException : BidRuleException
{
    public BidConcurrencyException(string message)
        : base("BID_CONFLICT", message)
    {
    }
}
