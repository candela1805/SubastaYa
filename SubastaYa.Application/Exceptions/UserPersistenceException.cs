namespace SubastaYa.Application.Exceptions;

public sealed class UserPersistenceException : Exception
{
    public UserPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}