using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions/{subastaId:guid}/bids")]
public class BidsController : ControllerBase
{
    private readonly IBidService _bidService;
    private readonly ICurrentUserService _currentUserService;

    public BidsController(
        IBidService bidService,
        ICurrentUserService currentUserService)
    {
        _bidService = bidService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BidHistoryResponse>>> GetHistory(
        Guid subastaId,
        CancellationToken cancellationToken)
    {
        try
        {
            var bids = await _bidService.GetBidHistoryAsync(
                subastaId,
                cancellationToken);

            return Ok(bids);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<BidResponse>> PlaceBid(
        Guid subastaId,
        [FromBody] PlaceBidRequest request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "No se pudo identificar al usuario autenticado."
            });
        }

        try
        {
            var result = await _bidService.PlaceBidAsync(
                subastaId,
                userId,
                request,
                cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }

        catch (BidConcurrencyException ex)
        {
            return Conflict(new { message = ex.Message });
        }

        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
