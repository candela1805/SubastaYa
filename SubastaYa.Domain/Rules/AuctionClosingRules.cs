using SubastaYa.Domain.Models;

namespace SubastaYa.Domain.Rules;

public enum AuctionClosingEligibility
{
    NotDue,
    AlreadyProcessed,
    Ready
}

public static class AuctionClosingRules
{
    public static AuctionClosingEligibility EvaluarEstado(
        Subasta subasta,
        DateTimeOffset ahora)
    {
        if (subasta.FechaFinUtc > ahora)
        {
            return AuctionClosingEligibility.NotDue;
        }

        if (subasta.Estado != EstadoSubasta.Activa)
        {
            return AuctionClosingEligibility.AlreadyProcessed;
        }

        return AuctionClosingEligibility.Ready;
    }

    public static void ValidarSinLiquidacion(
        Subasta subasta,
        bool tieneLiquidacion)
    {
        if (tieneLiquidacion)
        {
            throw new InvalidOperationException(
                $"La subasta {subasta.Id} sigue activa pero ya posee una liquidación.");
        }
    }

    public static void ValidarCantidadGanadoras(
        Subasta subasta,
        int cantidad)
    {
        if (cantidad > 1)
        {
            throw new InvalidOperationException(
                $"La subasta {subasta.Id} tiene más de una puja ganadora.");
        }
    }

    public static void DeclararDesierta(
        Subasta subasta,
        bool tienePujas,
        DateTimeOffset ahora)
    {
        ValidarCierrePermitido(subasta, ahora);

        if (tienePujas)
        {
            throw new InvalidOperationException(
                $"La subasta {subasta.Id} tiene pujas pero ninguna ganadora.");
        }

        subasta.Estado = EstadoSubasta.Desierta;
    }

    public static void Finalizar(
        Subasta subasta,
        DateTimeOffset ahora)
    {
        ValidarCierrePermitido(subasta, ahora);
        subasta.Estado = EstadoSubasta.Finalizada;
    }

    private static void ValidarCierrePermitido(
        Subasta subasta,
        DateTimeOffset ahora)
    {
        if (EvaluarEstado(subasta, ahora) != AuctionClosingEligibility.Ready)
        {
            throw new InvalidOperationException(
                $"La subasta {subasta.Id} debe estar activa y vencida para cerrarse.");
        }
    }
}