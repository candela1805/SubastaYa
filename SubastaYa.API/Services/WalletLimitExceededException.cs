namespace SubastaYa.API.Services;

public sealed class WalletLimitExceededException : Exception
{
    public WalletLimitExceededException(string message)
        : base(message)
    {
    }
}
