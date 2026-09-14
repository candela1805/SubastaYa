namespace SubastaYa.API.Services;

public sealed class BidNotFoundException : BidRuleException
{
    public BidNotFoundException(string code, string message)
        : base(code, message)
    {
    }
}
