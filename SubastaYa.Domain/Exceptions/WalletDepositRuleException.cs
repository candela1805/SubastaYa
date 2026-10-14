namespace SubastaYa.Domain.Exceptions;

public sealed class WalletDepositRuleException : Exception
{
    public bool IsLimitExceeded { get; }

    public WalletDepositRuleException(
        string message,
        bool isLimitExceeded = false)
        : base(message)
    {
        IsLimitExceeded = isLimitExceeded;
    }
}