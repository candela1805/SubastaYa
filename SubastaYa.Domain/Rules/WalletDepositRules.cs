using SubastaYa.Domain.Exceptions;
using SubastaYa.Domain.Models;

namespace SubastaYa.Domain.Rules;

public static class WalletDepositRules
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;

    public static void ValidarMonto(decimal monto)
    {
        if (monto <= 0)
        {
            throw new WalletDepositRuleException(
                "El monto del depósito debe ser mayor a 0.");
        }

        if (decimal.Round(monto, 2) != monto)
        {
            throw new WalletDepositRuleException(
                "El monto del depósito debe tener como máximo dos decimales.");
        }

        if (monto > MaxMoneyAmount)
        {
            throw new WalletDepositRuleException(
                "El depósito supera el límite monetario permitido para la billetera.",
                isLimitExceeded: true);
        }
    }

    public static void Depositar(Billetera billetera, decimal monto)
    {
        ValidarMonto(monto);

        if (billetera.SaldoTotal > MaxMoneyAmount - monto ||
            billetera.SaldoDisponible > MaxMoneyAmount - monto)
        {
            throw new WalletDepositRuleException(
                "El depósito supera el límite monetario permitido para la billetera.",
                isLimitExceeded: true);
        }

        billetera.SaldoTotal += monto;
        billetera.SaldoDisponible += monto;
    }
}