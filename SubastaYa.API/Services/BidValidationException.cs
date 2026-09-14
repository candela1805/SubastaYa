namespace SubastaYa.API.Services;

public sealed class BidValidationException : BidRuleException
{
    public BidValidationException(string code, string message)
        : base(code, message)
    {
    }
}
