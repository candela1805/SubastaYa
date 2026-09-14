namespace SubastaYa.API.Services;

public sealed class BidStateConflictException : BidRuleException
{
    public BidStateConflictException(string code, string message)
        : base(code, message)
    {
    }
}
