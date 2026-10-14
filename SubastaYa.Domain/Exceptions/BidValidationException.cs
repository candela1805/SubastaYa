namespace SubastaYa.Domain.Exceptions;

public sealed class BidValidationException : BidRuleException
{
    public BidValidationException(string code, string message)
        : base(code, message)
    {
    }
}
