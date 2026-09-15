namespace SubastaYa.API.Services;

public sealed class WalletConcurrencyException : Exception
{
    public WalletConcurrencyException(string message) :
        base(message)
    {
    }
}
