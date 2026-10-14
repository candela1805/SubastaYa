namespace SubastaYa.Application.Features.Auth;

public sealed class AuthValidationException : Exception
{
    public AuthValidationException(string message)
        : base(message)
    {
    }
}

public sealed class AuthConflictException : Exception
{
    public AuthConflictException(string message)
        : base(message)
    {
    }

    public AuthConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}