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

    public async Task<IReadOnlyList<WalletTransactionResponse>?>
        ObtenerMovimientosAsync(
            Guid usuarioId,
            CancellationToken cancellationToken = default)
    {
        var billeteraId = await _dbContext.Billeteras
            .AsNoTracking()
            .Where(billetera => billetera.UsuarioId == usuarioId)
            .Select(billetera => (Guid?)billetera.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!billeteraId.HasValue)
            return null;

        var movimientos = await _dbContext.TransaccionLedgers
            .AsNoTracking()
            .Where(movimiento => movimiento.BilleteraId == billeteraId.Value)
            .OrderByDescending(movimiento => movimiento.FechaUtc)
            .ThenByDescending(movimiento => movimiento.Id)
            .Select(movimiento => new
            {
                movimiento.Id,
                movimiento.Tipo,
                movimiento.Monto,
                movimiento.Descripcion,
                movimiento.FechaUtc
            })
            .ToListAsync(cancellationToken);

        return movimientos.Select(movimiento => new WalletTransactionResponse
        {
            Id = movimiento.Id,
            Tipo = movimiento.Tipo.ToString(),
            Monto = movimiento.Monto,
            Descripcion = movimiento.Descripcion,
            FechaUtc = movimiento.FechaUtc
        }).ToList();
    }

    public async Task<RetainedFundsResponse?> ObtenerFondosRetenidosAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        var billetera = await _dbContext.Billeteras
            .AsNoTracking()
            .Where(item => item.UsuarioId == usuarioId)
            .Select(item => new { item.SaldoRetenido })
            .FirstOrDefaultAsync(cancellationToken);

        if (billetera is null)
            return null;

        var retenciones = await (
                from puja in _dbContext.Pujas.AsNoTracking()
                join subasta in _dbContext.Subastas.AsNoTracking()
                    on puja.SubastaId equals subasta.Id
                where puja.UsuarioId == usuarioId &&
                      puja.EsGanadora &&
                      (subasta.Estado == EstadoSubasta.Programada ||
                       subasta.Estado == EstadoSubasta.Activa)
                orderby puja.FechaUtc descending, puja.Id descending
                select new
                {
                    SubastaId = subasta.Id,
                    Subasta = subasta.Titulo,
                    Monto = puja.Monto,
                    FechaUtc = puja.FechaUtc,
                    subasta.Estado
                })
            .ToListAsync(cancellationToken);

        var items = retenciones.Select(retencion => new RetainedFundItemResponse
        {
            SubastaId = retencion.SubastaId,
            Subasta = retencion.Subasta,
            Monto = retencion.Monto,
            FechaUtc = retencion.FechaUtc,
            Estado = retencion.Estado.ToString()
        }).ToList();

        var identificado = items.Sum(item => item.Monto);

        return new RetainedFundsResponse
        {
            TotalRetenido = billetera.SaldoRetenido,
            TotalIdentificado = identificado,
            Diferencia = billetera.SaldoRetenido - identificado,
            Items = items
        };
    }
}
