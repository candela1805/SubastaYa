using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SubastaYa.API.Contracts.Wallet;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly ICurrentUserService _currentUserService;

    public WalletController(
        IWalletService walletService,
        ICurrentUserService currentUserService)
    {
        _walletService = walletService;
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

        var saldo = await _walletService.ObtenerSaldoAsync(
            userId,
            cancellationToken);

        if (saldo is null)
            return NotFound("No se encontró la billetera del usuario.");

        return Ok(saldo);
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

        if (request.Monto > WalletService.MaxMoneyAmount)
            return BadRequest(
                "El monto supera el límite monetario permitido.");
        try
        {

            var saldo = await _walletService.DepositarAsync(
                userId,
                request.Monto,
                cancellationToken);

            if (saldo is null)
                return NotFound(
                    "No se encontró la billetera del usuario.");

            return Ok(saldo);
        }

        catch (WalletConcurrencyException ex)
        {
            return Conflict(ex.Message);
        }
        catch (WalletLimitExceededException ex)
        {
            return BadRequest(ex.Message);
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

        var movimientos = await _walletService.ObtenerMovimientosAsync(
            userId,
            cancellationToken);

        return movimientos is null
            ? NotFound("No se encontró la billetera del usuario.")
            : Ok(movimientos);
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

        var fondos = await _walletService.ObtenerFondosRetenidosAsync(
            userId,
            cancellationToken);

        return fondos is null
            ? NotFound("No se encontró la billetera del usuario.")
            : Ok(fondos);
    }
}
