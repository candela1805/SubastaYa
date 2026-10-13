using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SubastaYa.API.Contracts.Auctions;
using SubastaYa.Domain.Models;
using SubastaYa.API.Services;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Application.Features.Auctions.Queries;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions")]
public sealed class AuctionsController : ControllerBase
{
    private readonly IQueryHandler<SearchAuctionsQuery, PagedAuctionResult> _searchQuery;
    private readonly IQueryHandler<GetAuctionCategoriesQuery, IReadOnlyList<string>> _categoriesQuery;
    private readonly IQueryHandler<GetSellerAuctionsQuery, IReadOnlyList<AuctionResult>> _sellerAuctionsQuery;
    private readonly IQueryHandler<GetUserBidActivitiesQuery, IReadOnlyList<BidActivityResult>> _bidActivitiesQuery;
    private readonly ICommandHandler<CreateAuctionCommand, AuctionResult> _createCommand;
    private readonly ICommandHandler<UpdateAuctionCommand, AuctionResult> _updateCommand;
    private readonly ICommandHandler<DeleteAuctionCommand, bool> _deleteCommand;
    private readonly ICurrentUserService _currentUserService;

    public AuctionsController(
        IQueryHandler<SearchAuctionsQuery, PagedAuctionResult> searchQuery,
        IQueryHandler<GetAuctionCategoriesQuery, IReadOnlyList<string>> categoriesQuery,
        IQueryHandler<GetSellerAuctionsQuery, IReadOnlyList<AuctionResult>> sellerAuctionsQuery,
        IQueryHandler<GetUserBidActivitiesQuery, IReadOnlyList<BidActivityResult>> bidActivitiesQuery,
        ICommandHandler<CreateAuctionCommand, AuctionResult> createCommand,
        ICommandHandler<UpdateAuctionCommand, AuctionResult> updateCommand,
        ICommandHandler<DeleteAuctionCommand, bool> deleteCommand,
        ICurrentUserService currentUserService)
    {
        _searchQuery = searchQuery;
        _categoriesQuery = categoriesQuery;
        _sellerAuctionsQuery = sellerAuctionsQuery;
        _bidActivitiesQuery = bidActivitiesQuery;
        _createCommand = createCommand;
        _updateCommand = updateCommand;
        _deleteCommand = deleteCommand;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedAuctionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedAuctionResponse>> ObtenerSubastas(
        string? buscar,
        decimal? precioMin,
        decimal? precioMax,
        string? categoria,
        EstadoSubasta? estado,
        string? ordenar,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (precioMin < 0 || precioMax < 0)
            return BadRequest("Los precios no pueden ser negativos.");

        if (precioMin.HasValue && precioMax.HasValue && precioMin > precioMax)
            return BadRequest("El precio mÃ­nimo no puede ser mayor al precio mÃ¡ximo.");

        if (!string.IsNullOrWhiteSpace(ordenar) && ordenar is not ("tiempo" or "precio"))
            return BadRequest("El orden debe ser 'tiempo' o 'precio'.");

        if (page < 1)
            return BadRequest("La pÃ¡gina debe ser mayor o igual a 1.");

        if (pageSize is < 1 or > 50)
            return BadRequest("El tamaÃ±o de pÃ¡gina debe estar entre 1 y 50.");

        var result = await _searchQuery.HandleAsync(new SearchAuctionsQuery(
            buscar, precioMin, precioMax, categoria, estado, ordenar,
            page, pageSize), cancellationToken);

        var resultado = new PagedAuctionResponse
        {
            Items = result.Items.Select(ToResponse).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems,
            TotalPages = result.TotalPages
        };

        return Ok(resultado);
    }

    [HttpGet("categories")]
    [ProducesResponseType(
        typeof(IReadOnlyList<string>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> ObtenerCategorias(
        CancellationToken cancellationToken)
    {
        return Ok(await _categoriesQuery.HandleAsync(
            new GetAuctionCategoriesQuery(), cancellationToken));
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(AuctionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuctionResponse>> CrearSubasta(
        CreateAuctionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "No se pudo identificar al usuario autenticado."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Titulo))
            return BadRequest(AuctionCreationRules.TitleRequired);

        if (string.IsNullOrWhiteSpace(request.Descripcion))
            return BadRequest(AuctionCreationRules.DescriptionRequired);

        if (string.IsNullOrWhiteSpace(request.Categoria))
            return BadRequest(AuctionCreationRules.CategoryRequired);

        try
        {
            var result = await _createCommand.HandleAsync(
                new CreateAuctionCommand(
                    userId,
                    request.Titulo,
                    request.Descripcion,
                    request.ImagenUrl,
                    request.Categoria,
                    request.PrecioInicial,
                    request.IncrementoMinimo,
                    request.FechaInicioUtc,
                    request.FechaFinUtc),
                cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                ToResponse(result));
        }
        catch (AuctionValidationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(
        typeof(IReadOnlyList<AuctionResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AuctionResponse>>>
        ObtenerMisPublicaciones(CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "No se pudo identificar al usuario autenticado."
            });
        }

        var result = await _sellerAuctionsQuery.HandleAsync(
            new GetSellerAuctionsQuery(userId), cancellationToken);

        return Ok(result.Select(ToResponse).ToList());
    }

    [HttpGet("my-bids")]
    [Authorize]
    [ProducesResponseType(
        typeof(IReadOnlyList<BidActivityResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<BidActivityResponse>>>
        ObtenerMisPujas(CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "No se pudo identificar al usuario autenticado."
            });
        }

        var result = await _bidActivitiesQuery.HandleAsync(
            new GetUserBidActivitiesQuery(userId), cancellationToken);
        return Ok(result.Select(ToResponse).ToList());
    }

    [HttpPut("{subastaId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(AuctionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuctionResponse>> ActualizarPublicacion(
        Guid subastaId,
        UpdateAuctionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Titulo) ||
            string.IsNullOrWhiteSpace(request.Descripcion) ||
            string.IsNullOrWhiteSpace(request.Categoria))
        {
            return BadRequest(new
            {
                message = "TÃ­tulo, descripciÃ³n y categorÃ­a son obligatorios."
            });
        }

        try
        {
            var result = await _updateCommand.HandleAsync(
                new UpdateAuctionCommand(subastaId, userId, request.Titulo,
                    request.Descripcion, request.ImagenUrl, request.Categoria,
                    request.FechaFinUtc), cancellationToken);
            return Ok(ToResponse(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (SubastaYa.Application.Features.Auctions.Commands.AuctionOperationConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{subastaId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EliminarPublicacion(
        Guid subastaId,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
            return Unauthorized();

        try
        {
            await _deleteCommand.HandleAsync(
                new DeleteAuctionCommand(subastaId, userId), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (SubastaYa.Application.Features.Auctions.Commands.AuctionOperationConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private static AuctionResponse ToResponse(AuctionResult result) => new()
    {
        Id = result.Id, Titulo = result.Titulo, Descripcion = result.Descripcion,
        ImagenUrl = result.ImagenUrl, Categoria = result.Categoria,
        PrecioInicial = result.PrecioInicial, PrecioActual = result.PrecioActual,
        IncrementoMinimo = result.IncrementoMinimo, FechaInicioUtc = result.FechaInicioUtc,
        FechaFinUtc = result.FechaFinUtc, Estado = result.Estado
    };

    private static BidActivityResponse ToResponse(BidActivityResult result) => new()
    {
        SubastaId = result.SubastaId, Titulo = result.Titulo, Descripcion = result.Descripcion,
        ImagenUrl = result.ImagenUrl, Categoria = result.Categoria, PrecioInicial = result.PrecioInicial,
        MiOferta = result.MiOferta, OfertaActual = result.OfertaActual,
        IncrementoMinimo = result.IncrementoMinimo, FechaInicioUtc = result.FechaInicioUtc,
        FechaFinUtc = result.FechaFinUtc, Estado = result.Estado, Resultado = result.Resultado
    };
}
