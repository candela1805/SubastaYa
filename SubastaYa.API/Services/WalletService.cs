using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Wallet;
using SubastaYa.API.Data;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public sealed class WalletService : IWalletService
{
    public const decimal MaxMoneyAmount = 9_999_999_999_999_999.99m;

    private readonly ApplicationDbContext _dbContext;

    public WalletService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WalletBalanceResponse?> ObtenerSaldoAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Billeteras
            .AsNoTracking()
            .Where(billetera => billetera.UsuarioId == usuarioId)
            .Select(billetera => new WalletBalanceResponse
            {
                Total = billetera.SaldoTotal,
                Retenido = billetera.SaldoRetenido,
                Disponible = billetera.SaldoDisponible
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<WalletBalanceResponse?> DepositarAsync(
    Guid usuarioId,
    decimal monto,
    CancellationToken cancellationToken = default)
    {
        if (monto <= 0)
            throw new ArgumentException(
                "El monto del depósito debe ser mayor a 0.",
                nameof(monto));

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var billetera = await _dbContext.Billeteras
                .FirstOrDefaultAsync(
                    billetera => billetera.UsuarioId == usuarioId,
                    cancellationToken);

            if (billetera is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            if (monto > MaxMoneyAmount ||
                billetera.SaldoTotal > MaxMoneyAmount - monto ||
                billetera.SaldoDisponible > MaxMoneyAmount - monto)
            {
                throw new WalletLimitExceededException(
                    "El depósito supera el límite monetario permitido para la billetera.");
            }

            billetera.SaldoTotal += monto;
            billetera.SaldoDisponible += monto;

            var movimiento = new TransaccionLedger
            {
                BilleteraId = billetera.Id,
                Tipo = TipoMovimientoBilletera.Deposito,
                Monto = monto,
                Descripcion = "Depósito de saldo",
                FechaUtc = DateTimeOffset.UtcNow
            };

            _dbContext.TransaccionLedgers.Add(movimiento);

            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new WalletBalanceResponse
            {
                Total = billetera.SaldoTotal,
                Retenido = billetera.SaldoRetenido,
                Disponible = billetera.SaldoDisponible
            };
        }

        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);

            throw new WalletConcurrencyException(
                "La billetera fue modificada por otra operación. Intente nuevamente.");
        }

        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
