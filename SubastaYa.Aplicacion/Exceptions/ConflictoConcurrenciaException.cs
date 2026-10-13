namespace SubastaYa.Aplicacion.Exceptions;

public sealed class ConflictoConcurrenciaException : PersistenciaException
{
    public ConflictoConcurrenciaException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}