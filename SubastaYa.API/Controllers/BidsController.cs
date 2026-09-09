using Microsoft.AspNetCore.Mvc;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions/{subastaId:guid}/bids")]
public class BidsController : ControllerBase
{
    private readonly IBidService _bidService;

    private static readonly Guid UsuarioDemoId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public BidsController(IBidService bidService)
    {
        _bidService = bidService;
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
    public async Task<ActionResult<BidResponse>> PlaceBid(
        Guid subastaId,
        [FromBody] PlaceBidRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _bidService.PlaceBidAsync(
                subastaId,
                UsuarioDemoId,
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
