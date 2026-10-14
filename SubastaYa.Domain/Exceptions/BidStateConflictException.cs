namespace SubastaYa.Domain.Exceptions;

public sealed class BidStateConflictException : BidRuleException
{
    public BidStateConflictException(string code, string message)
        : base(code, message)
    {
    }
}
