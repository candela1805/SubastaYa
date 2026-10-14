namespace SubastaYa.Domain.Exceptions;

public sealed class BidForbiddenException : BidRuleException
{
    public BidForbiddenException(string code, string message)
        : base(code, message)
    {
    }
}
