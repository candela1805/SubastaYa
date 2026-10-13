namespace SubastaYa.Application.Features.Users;

public sealed class UserProfileValidationException : Exception
{
    public UserProfileValidationException(string message)
        : base(message)
    {
    }
}

public sealed class UserProfileConflictException : Exception
{
    public UserProfileConflictException(string message)
        : base(message)
    {
    }

    public UserProfileConflictException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}