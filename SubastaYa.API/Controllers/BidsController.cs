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

    [HttpGet("state")]
    [Authorize]
    public async Task<ActionResult<BidRoomStateResponse>> GetRoomState(
        Guid subastaId,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(new BidErrorResponse
            {
                Code = "UNAUTHORIZED",
                Message = "No se pudo identificar al usuario autenticado."
            });
        }

        try
        {
            var state = await _bidService.GetRoomStateAsync(
                subastaId,
                userId,
                cancellationToken);

            return Ok(state);
        }
        catch (BidNotFoundException ex)
        {
            return NotFound(ToErrorResponse(ex));
        }
        catch (BidStateConflictException ex)
        {
            return Conflict(ToErrorResponse(ex));
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
            return Unauthorized(new BidErrorResponse
            {
                Code = "UNAUTHORIZED",
                Message = "No se pudo identificar al usuario autenticado."
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
        catch (BidNotFoundException ex)
        {
            return NotFound(ToErrorResponse(ex));
        }

        catch (BidForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ToErrorResponse(ex));
        }

        catch (BidStateConflictException ex)
        {
            return Conflict(ToErrorResponse(ex));
        }

        catch (BidConcurrencyException ex)
        {
            return Conflict(ToErrorResponse(ex));
        }

        catch (BidInsufficientFundsException ex)
        {
            return UnprocessableEntity(ToErrorResponse(ex));
        }

        catch (BidValidationException ex)
        {
            return BadRequest(ToErrorResponse(ex));
        }
    }

    private static BidErrorResponse ToErrorResponse(BidRuleException exception)
    {
        return new BidErrorResponse
        {
            Code = exception.Code,
            Message = exception.Message
        };
    }
}
