using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Services;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Bids.Commands.PlaceBid;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Application.Features.Bids.Queries.GetBidHistory;
using SubastaYa.Application.Features.Bids.Queries.GetRoomState;
using BidNotFoundException = SubastaYa.Application.Exceptions.BidNotFoundException;
using BidStateConflictException = SubastaYa.Application.Exceptions.BidStateConflictException;
using BidForbiddenException = SubastaYa.Application.Exceptions.BidForbiddenException;
using BidConcurrencyException = SubastaYa.Application.Exceptions.BidConcurrencyException;
using BidInsufficientFundsException = SubastaYa.Application.Exceptions.BidInsufficientFundsException;
using BidValidationException = SubastaYa.Application.Exceptions.BidValidationException;
using BidRuleException = SubastaYa.Application.Exceptions.BidRuleException;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions/{subastaId:guid}/bids")]
public class BidsController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ICommandHandler<PlaceBidCommand, PlaceBidResult> _placeBidHandler;
    private readonly IQueryHandler<GetBidHistoryQuery, IReadOnlyList<BidHistoryResult>> _getBidHistoryHandler;
    private readonly IQueryHandler<GetRoomStateQuery, BidRoomStateResult> _getRoomStateHandler;

    public BidsController(
        ICurrentUserService currentUserService,
        ICommandHandler<PlaceBidCommand, PlaceBidResult> placeBidHandler,
        IQueryHandler<GetBidHistoryQuery, IReadOnlyList<BidHistoryResult>> getBidHistoryHandler,
        IQueryHandler<GetRoomStateQuery, BidRoomStateResult> getRoomStateHandler)
    {
        _currentUserService = currentUserService;
        _placeBidHandler = placeBidHandler;
        _getBidHistoryHandler = getBidHistoryHandler;
        _getRoomStateHandler = getRoomStateHandler;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BidHistoryResponse>>> GetHistory(
        Guid subastaId,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new GetBidHistoryQuery(subastaId);

            var bids = await _getBidHistoryHandler.HandleAsync(
                query,
                cancellationToken);

            return Ok(bids.Select(bid => new BidHistoryResponse
            {
                Id = bid.Id,
                Monto = bid.Monto,
                Pseudonimo = bid.Pseudonimo,
                FechaUtc = bid.FechaUtc
            }).ToList());
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
            var query = new GetRoomStateQuery(subastaId, userId);

            var result = await _getRoomStateHandler.HandleAsync(
                query,
                cancellationToken);

            var state = new BidRoomStateResponse
            {
                SubastaId = result.SubastaId,
                PrecioActual = result.PrecioActual,
                IncrementoMinimo = result.IncrementoMinimo,
                PujaMinimaSiguiente = result.PujaMinimaSiguiente,
                FechaFinUtc = result.FechaFinUtc,
                EstadoSubasta = result.EstadoSubasta,
                EstadoPostor = result.EstadoPostor
            };

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
            var command = new PlaceBidCommand(
            subastaId,
            userId,
            request.Monto);

            var result = await _placeBidHandler.HandleAsync(
                command,
                cancellationToken);

            return Ok(new BidResponse
            {
                Id = result.Id,
                SubastaId = result.SubastaId,
                Monto = result.Monto,
                Pseudonimo = result.Pseudonimo,
                FechaUtc = result.FechaUtc,
                PrecioActual = result.PrecioActual,
                FechaFinUtc = result.FechaFinUtc,
                SubastaExtendida = result.SubastaExtendida,
                Estado = result.Estado,
                IncrementoMinimo = result.IncrementoMinimo,
                PujaMinimaSiguiente = result.PujaMinimaSiguiente
            });
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
