using SubastaYa.Domain.Exceptions;
using SubastaYa.Domain.Models;

namespace SubastaYa.Domain.Rules;

public static class AuctionPublicationRules
{
    public static void ValidarEstadoParaEditar(Subasta subasta)
    {
        if (subasta.Estado is EstadoSubasta.Finalizada or EstadoSubasta.Desierta)
        {
            throw new AuctionPublicationRuleException(
                "Las publicaciones finalizadas o desiertas no pueden editarse.");
        }
    }

    public static void ValidarEdicion(
        Subasta subasta,
        bool tienePujas,
        DateTimeOffset fechaFin,
        DateTimeOffset ahora)
    {
        ValidarEstadoParaEditar(subasta);

        if (tienePujas)
        {
            throw new AuctionPublicationRuleException(
                "La publicación no puede editarse porque ya recibió pujas.");
        }

        if (fechaFin <= ahora || fechaFin <= subasta.FechaInicioUtc)
        {
            throw new AuctionPublicationRuleException(
                "La fecha de finalización debe ser futura y posterior al inicio.");
        }
    }

    public static void ValidarEliminacion(
        bool tienePujas,
        bool tieneLiquidacion,
        bool tieneAuditoria)
    {
        if (tienePujas)
        {
            throw new AuctionPublicationRuleException(
                "Esta publicación no puede eliminarse porque ya recibió una puja.");
        }

        if (tieneLiquidacion)
        {
            throw new AuctionPublicationRuleException(
                "Esta publicación no puede eliminarse porque tiene una liquidación asociada.");
        }

        if (tieneAuditoria)
        {
            throw new AuctionPublicationRuleException(
                "Esta publicación no puede eliminarse porque posee historial de auditoría inmutable.");
        }
    }
}