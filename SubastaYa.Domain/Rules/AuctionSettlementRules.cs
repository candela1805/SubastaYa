using SubastaYa.Domain.Models;

namespace SubastaYa.Domain.Rules;

public static class AuctionSettlementRules
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;

    public static Guid ValidarGanador(
    Subasta subasta,
    Puja ganador,
    IEnumerable<decimal> montos)
    {
        if (ganador.Monto != montos.Max() ||
            ganador.Monto != subasta.PrecioActual ||
            ganador.Monto <= 0)
        {
            throw new InvalidOperationException(
                $"La puja ganadora de la subasta {subasta.Id} no coincide con su precio final.");
        }

        if (!subasta.VendedorId.HasValue)
        {
            throw new InvalidOperationException(
                $"La subasta {subasta.Id} no tiene vendedor asignado.");
        }

        var vendedorId = subasta.VendedorId.Value;

        if (vendedorId == ganador.UsuarioId)
        {
            throw new InvalidOperationException(
                $"El vendedor de la subasta {subasta.Id} no puede ser su comprador.");
        }

        return vendedorId;
    }
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