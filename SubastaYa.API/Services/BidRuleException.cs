namespace SubastaYa.API.Services;

public abstract class BidRuleException : Exception
{
    protected BidRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
