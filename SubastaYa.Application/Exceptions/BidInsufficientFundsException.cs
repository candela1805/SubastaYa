namespace SubastaYa.Application.Exceptions;

public sealed class BidInsufficientFundsException : BidRuleException
{
    public BidInsufficientFundsException(string code, string message)
        : base(code, message)
    {
    }
}
