using SubastaYa.Domain.Models;

namespace SubastaYa.Domain.Rules;

public static class AuctionSettlementRules
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;

    public static void TransferirFondos(
        Guid subastaId,
        Billetera comprador,
        Billetera vendedor,
        decimal importe)
    {
        if (comprador.SaldoRetenido < importe ||
            comprador.SaldoTotal < importe)
        {
            throw new InvalidOperationException(
                $"La retención del comprador para la subasta {subastaId} es insuficiente.");
        }

        if (vendedor.SaldoTotal > MaxMoneyAmount - importe ||
            vendedor.SaldoDisponible > MaxMoneyAmount - importe)
        {
            throw new InvalidOperationException(
                $"El crédito de la subasta {subastaId} supera el límite monetario del vendedor.");
        }

        comprador.SaldoTotal -= importe;
        comprador.SaldoRetenido -= importe;

        vendedor.SaldoTotal += importe;
        vendedor.SaldoDisponible += importe;
    }
}