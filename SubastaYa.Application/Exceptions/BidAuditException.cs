namespace SubastaYa.Application.Exceptions;

public sealed class BidAuditException : Exception
{
    public BidRuleException OriginalRejection { get; }

    public BidAuditException(
        BidRuleException originalRejection,
        Exception auditFailure)
        : base(
            $"No se pudo registrar la auditoría del rechazo " +
            $"'{originalRejection.Code}'.",
            auditFailure)
    {
        OriginalRejection = originalRejection;
    }
}