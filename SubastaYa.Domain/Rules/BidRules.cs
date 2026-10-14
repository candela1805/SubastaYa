using SubastaYa.Domain.Exceptions;
using SubastaYa.Domain.Models;

namespace SubastaYa.Domain.Rules;

public static class BidRules
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;

    public static decimal CalcularPujaMinima(Subasta subasta)
    {
        if (subasta.PrecioActual < 0 ||
            subasta.IncrementoMinimo <= 0 ||
            subasta.PrecioActual >
                MaxMoneyAmount - subasta.IncrementoMinimo)
        {
            throw new BidStateConflictException(
                "AUCTION_BID_CONFIGURATION_INVALID",
                "La configuración monetaria de la subasta no permite nuevas pujas.");
        }

        return subasta.PrecioActual + subasta.IncrementoMinimo;
    }

    public static decimal ValidarMonto(decimal? montoSolicitado)
    {
        if (!montoSolicitado.HasValue || montoSolicitado.Value <= 0)
        {
            throw new BidValidationException(
                "INVALID_BID_AMOUNT",
                "El monto de la puja debe ser mayor a 0.");
        }

        var monto = montoSolicitado.Value;
        var escala = (decimal.GetBits(monto)[3] >> 16) & 0xFF;

        if (escala > 2)
        {
            throw new BidValidationException(
                "INVALID_BID_SCALE",
                "El monto de la puja puede tener como máximo 2 decimales.");
        }

        if (monto > MaxMoneyAmount)
        {
            throw new BidValidationException(
                "BID_AMOUNT_LIMIT_EXCEEDED",
                "El monto de la puja supera el límite monetario permitido.");
        }

        return monto;
    }

    public static void ValidarSubastaParaPujar(
    Subasta subasta,
    Guid usuarioId,
    DateTimeOffset ahora)
    {
        if (subasta.VendedorId == usuarioId)
        {
            throw new BidForbiddenException(
                "OWN_AUCTION_BID",
                "No podés pujar en tu propia subasta.");
        }

        if (subasta.Estado != EstadoSubasta.Activa)
        {
            throw new BidStateConflictException(
                "AUCTION_NOT_ACTIVE",
                "La subasta no está activa.");
        }

        if (ahora < subasta.FechaInicioUtc)
        {
            throw new BidStateConflictException(
                "AUCTION_NOT_STARTED",
                "La subasta todavía no comenzó.");
        }

        if (ahora >= subasta.FechaFinUtc)
        {
            throw new BidStateConflictException(
                "AUCTION_ENDED",
                "La subasta ya finalizó.");
        }
    }

    public static Puja? ValidarConsistenciaPujas(
    Subasta subasta,
    IReadOnlyList<Puja> pujasGanadoras,
    bool existenPujas)
    {
        if (pujasGanadoras.Count > 1)
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "La subasta tiene más de una puja ganadora registrada.");
        }

        var pujaAnterior = pujasGanadoras.SingleOrDefault();

        if (pujaAnterior is null && existenPujas)
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "La subasta tiene pujas, pero ninguna está marcada como ganadora.");
        }

        if (pujaAnterior is not null &&
            pujaAnterior.Monto != subasta.PrecioActual)
        {
            throw new BidStateConflictException(
                "BID_STATE_INCONSISTENT",
                "El estado actual de la subasta es inconsistente. Actualizá los datos e intentá nuevamente.");
        }

        return pujaAnterior;
    }

    public static decimal ValidarFondosParaPujar(
    Billetera? billetera,
    Puja? pujaAnterior,
    Guid usuarioId,
    decimal monto)
    {
        if (billetera is null)
        {
            throw new BidInsufficientFundsException(
                "WALLET_NOT_FOUND",
                "El usuario no tiene una billetera disponible.");
        }

        var mismoLider = pujaAnterior is not null &&
                         pujaAnterior.UsuarioId == usuarioId;

        var montoARequerir = mismoLider
            ? monto - pujaAnterior!.Monto
            : monto;

        if (mismoLider &&
            billetera.SaldoRetenido < pujaAnterior!.Monto)
        {
            throw new BidStateConflictException(
                "ESCROW_BALANCE_INCONSISTENT",
                "El saldo retenido del postor actual es inconsistente.");
        }

        if (billetera.SaldoDisponible < montoARequerir)
        {
            throw new BidInsufficientFundsException(
                "INSUFFICIENT_BALANCE",
                "Saldo disponible insuficiente para realizar la puja.");
        }

        return montoARequerir;
    }

    public static void ValidarBilleteraGanadorAnterior(
    Billetera? billeteraAnterior,
    Puja pujaAnterior)
    {
        if (billeteraAnterior is null)
        {
            throw new BidStateConflictException(
                "PREVIOUS_BIDDER_WALLET_NOT_FOUND",
                "El ganador anterior no tiene billetera.");
        }

        if (billeteraAnterior.SaldoRetenido < pujaAnterior.Monto)
        {
            throw new BidStateConflictException(
                "ESCROW_BALANCE_INCONSISTENT",
                "El saldo retenido del ganador anterior es inconsistente.");
        }
    }

    public static TransaccionLedger LiberarFondos(
    Billetera billeteraAnterior,
    Puja pujaAnterior,
    Guid nuevaPujaId,
    Guid subastaId,
    DateTimeOffset ahora)
    {
        ValidarBilleteraGanadorAnterior(
            billeteraAnterior,
            pujaAnterior);

        billeteraAnterior.SaldoRetenido -= pujaAnterior.Monto;
        billeteraAnterior.SaldoDisponible += pujaAnterior.Monto;

        return new TransaccionLedger
        {
            Id = Guid.NewGuid(),
            BilleteraId = billeteraAnterior.Id,
            Tipo = TipoMovimientoBilletera.Liberacion,
            Monto = pujaAnterior.Monto,
            Descripcion =
                $"Liberación por puja {nuevaPujaId} que superó la oferta en la subasta {subastaId}.",
            FechaUtc = ahora
        };
    }

    public static TransaccionLedger RetenerFondos(
        Billetera billetera,
        decimal montoARequerir,
        Guid nuevaPujaId,
        Guid subastaId,
        DateTimeOffset ahora)
    {
        billetera.SaldoRetenido += montoARequerir;
        billetera.SaldoDisponible -= montoARequerir;

        return new TransaccionLedger
        {
            Id = Guid.NewGuid(),
            BilleteraId = billetera.Id,
            Tipo = TipoMovimientoBilletera.Retencion,
            Monto = montoARequerir,
            Descripcion =
                $"Retención por puja {nuevaPujaId} en la subasta {subastaId}.",
            FechaUtc = ahora
        };
    }

    public static AuditoriaLog? ExtenderSubastaSiCorresponde(
    Subasta subasta,
    Guid nuevaPujaId,
    decimal monto,
    DateTimeOffset ahora)
    {
        var tiempoRestante = subasta.FechaFinUtc - ahora;

        if (tiempoRestante > TimeSpan.FromSeconds(60))
        {
            return null;
        }

        subasta.FechaFinUtc = subasta.FechaFinUtc.AddMinutes(2);

        return new AuditoriaLog
        {
            Id = Guid.NewGuid(),
            SubastaId = subasta.Id,
            TipoEvento = "AuctionExtended",
            Detalle =
                $"La subasta fue extendida por 2 minutos tras la puja {nuevaPujaId} de {monto:F2} en los últimos 60 segundos.",
            FechaUtc = ahora
        };
    }

    public static AuditoriaLog CrearAuditoriaPujaConfirmada(
        Guid subastaId,
        Guid nuevaPujaId,
        Guid usuarioId,
        decimal monto,
        DateTimeOffset ahora)
    {
        return new AuditoriaLog
        {
            Id = Guid.NewGuid(),
            SubastaId = subastaId,
            TipoEvento = "BidPlaced",
            Detalle =
                $"Puja {nuevaPujaId} confirmada por {monto:F2} para el usuario {usuarioId}.",
            FechaUtc = ahora
        };
    }
}