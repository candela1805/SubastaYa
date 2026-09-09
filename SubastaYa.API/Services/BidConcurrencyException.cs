namespace SubastaYa.API.Services;

public sealed class BidConcurrencyException : Exception
{
    public BidConcurrencyException(string message) 
        : base(message)
    {
    }
}
