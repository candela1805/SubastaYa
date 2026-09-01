using Microsoft.AspNetCore.Mvc;
using SubastaYa.API.Contracts.Wallet;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;

    // Identidad temporal de Development hasta que el proyecto incorpore autenticación.
    private static readonly Guid UsuarioDemoId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpGet("balance")]
    [ProducesResponseType(
        typeof(WalletBalanceResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletBalanceResponse>> ObtenerSaldo(
        CancellationToken cancellationToken)
    {
        var saldo = await _walletService.ObtenerSaldoAsync(
            UsuarioDemoId,
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
        if (request.Monto <= 0)
            return BadRequest(
                "El monto del depósito debe ser mayor a 0.");

        if (request.Monto > WalletService.MaxMoneyAmount)
            return BadRequest(
                "El monto supera el límite monetario permitido.");
        try
        {

            var saldo = await _walletService.DepositarAsync(
                UsuarioDemoId,
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
}
