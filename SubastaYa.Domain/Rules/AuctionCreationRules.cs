using SubastaYa.Domain.Exceptions;

namespace SubastaYa.Domain.Rules;

public static class AuctionCreationRules
{
    public const string TitleRequired =
        "El título es obligatorio.";

    public const string DescriptionRequired =
        "La descripción es obligatoria.";

    public const string CategoryRequired =
        "La categoría es obligatoria.";

    public const string InitialPriceMustBePositive =
        "El precio inicial debe ser mayor a 0.";

    public const string MinimumIncrementMustBePositive =
        "El incremento mínimo debe ser mayor a 0.";

    public const string EndDateMustBeAfterStartDate =
        "La fecha de finalización debe ser posterior a la fecha de inicio.";

    public const string EndDateMustBeFuture =
        "La fecha de finalización debe ser futura.";

    public static void Validar(
        string titulo,
        string descripcion,
        string categoria,
        decimal precioInicial,
        decimal incrementoMinimo,
        DateTimeOffset fechaInicio,
        DateTimeOffset fechaFin,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            throw new AuctionCreationRuleException(TitleRequired);

        if (string.IsNullOrWhiteSpace(descripcion))
            throw new AuctionCreationRuleException(DescriptionRequired);

        if (string.IsNullOrWhiteSpace(categoria))
            throw new AuctionCreationRuleException(CategoryRequired);

        if (precioInicial <= 0)
            throw new AuctionCreationRuleException(InitialPriceMustBePositive);

        if (incrementoMinimo <= 0)
            throw new AuctionCreationRuleException(MinimumIncrementMustBePositive);

        if (fechaFin <= fechaInicio)
            throw new AuctionCreationRuleException(EndDateMustBeAfterStartDate);

        if (fechaFin <= ahora)
            throw new AuctionCreationRuleException(EndDateMustBeFuture);
    }
}