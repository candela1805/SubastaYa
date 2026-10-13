using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SubastaYa.API.Contracts.Wallet;
using SubastaYa.API.Services;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Wallet.Commands.DepositWallet;
using SubastaYa.Application.Features.Wallet.DTOs;
using SubastaYa.Application.Features.Wallet.Queries;

namespace SubastaYa.API.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly IQueryHandler<GetWalletBalanceQuery, WalletBalanceResult?> _balanceQuery;
    private readonly ICommandHandler<DepositWalletCommand, WalletBalanceResult?> _depositCommand;
    private readonly IQueryHandler<GetWalletTransactionsQuery, IReadOnlyList<WalletTransactionResult>?> _transactionsQuery;
    private readonly IQueryHandler<GetRetainedFundsQuery, RetainedFundsResult?> _retainedFundsQuery;
    private readonly ICurrentUserService _currentUserService;

    public WalletController(
        IQueryHandler<GetWalletBalanceQuery, WalletBalanceResult?> balanceQuery,
        ICommandHandler<DepositWalletCommand, WalletBalanceResult?> depositCommand,
        IQueryHandler<GetWalletTransactionsQuery, IReadOnlyList<WalletTransactionResult>?> transactionsQuery,
        IQueryHandler<GetRetainedFundsQuery, RetainedFundsResult?> retainedFundsQuery,
        ICurrentUserService currentUserService)
    {
        _balanceQuery = balanceQuery;
        _depositCommand = depositCommand;
        _transactionsQuery = transactionsQuery;
        _retainedFundsQuery = retainedFundsQuery;
        _currentUserService = currentUserService;
    }

    [HttpGet("balance")]
    [ProducesResponseType(
        typeof(WalletBalanceResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletBalanceResponse>> ObtenerSaldo(
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(
                "No se pudo identificar al usuario autenticado.");
        }

        var saldo = await _balanceQuery.HandleAsync(
            new GetWalletBalanceQuery(userId), cancellationToken);

        if (saldo is null)
            return NotFound("No se encontró la billetera del usuario.");

        return Ok(ToResponse(saldo));
    }

    [HttpPost("deposit")]
    [ProducesResponseType(
    typeof(WalletBalanceResponse),
    StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WalletBalanceResponse>> Depositar(
        DepositRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(
                "No se pudo identificar al usuario autenticado.");
        }

        if (request.Monto <= 0)
            return BadRequest(
                "El monto del depósito debe ser mayor a 0.");

        if (request.Monto > DepositWalletCommandHandler.MaxMoneyAmount)
            return BadRequest(
                "El monto supera el límite monetario permitido.");
        try
        {

            var saldo = await _depositCommand.HandleAsync(
                new DepositWalletCommand(
                    userId,
                    request.Monto,
                    idempotencyKey),
                cancellationToken);

            if (saldo is null)
                return NotFound(
                    "No se encontró la billetera del usuario.");

            return Ok(ToResponse(saldo));
        }

        catch (SubastaYa.Application.Features.Wallet.Commands.DepositWallet.WalletConcurrencyException ex)
        {
            return Conflict(ex.Message);
        }
        catch (SubastaYa.Application.Features.Wallet.Commands.DepositWallet.WalletLimitExceededException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (WalletValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (WalletIdempotencyConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("transactions")]
    [ProducesResponseType(
        typeof(IReadOnlyList<WalletTransactionResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WalletTransactionResponse>>>
        ObtenerMovimientos(CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
            return Unauthorized();

        var movimientos = await _transactionsQuery.HandleAsync(
            new GetWalletTransactionsQuery(userId), cancellationToken);

        return movimientos is null
            ? NotFound("No se encontró la billetera del usuario.")
            : Ok(movimientos.Select(item => new WalletTransactionResponse
            {
                Id = item.Id,
                Tipo = item.Tipo,
                Monto = item.Monto,
                Descripcion = item.Descripcion,
                FechaUtc = item.FechaUtc
            }).ToList());
    }

    [HttpGet("retained-funds")]
    [ProducesResponseType(
        typeof(RetainedFundsResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RetainedFundsResponse>>
        ObtenerFondosRetenidos(CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
            return Unauthorized();

        var fondos = await _retainedFundsQuery.HandleAsync(
            new GetRetainedFundsQuery(userId), cancellationToken);

        return fondos is null
            ? NotFound("No se encontró la billetera del usuario.")
            : Ok(new RetainedFundsResponse
            {
                TotalRetenido = fondos.TotalRetenido,
                TotalIdentificado = fondos.TotalIdentificado,
                Diferencia = fondos.Diferencia,
                Items = fondos.Items.Select(item => new RetainedFundItemResponse
                {
                    SubastaId = item.SubastaId,
                    Subasta = item.Subasta,
                    Monto = item.Monto,
                    FechaUtc = item.FechaUtc,
                    Estado = item.Estado
                }).ToList()
            });
    }

    private static WalletBalanceResponse ToResponse(WalletBalanceResult balance) => new()
    {
        Total = balance.Total,
        Retenido = balance.Retenido,
        Disponible = balance.Disponible
    };
}
