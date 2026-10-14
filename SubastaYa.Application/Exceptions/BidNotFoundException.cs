using SubastaYa.Domain.Exceptions;
namespace SubastaYa.Application.Exceptions;

public sealed class BidNotFoundException : BidRuleException
{
    public BidNotFoundException(string code, string message)
        : base(code, message)
    {
    }
}
