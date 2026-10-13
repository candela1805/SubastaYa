namespace SubastaYa.Aplicacion.Exceptions;

public class PersistenciaException : Exception
{
    public PersistenciaException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}