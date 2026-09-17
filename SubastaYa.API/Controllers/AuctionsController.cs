using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SubastaYa.API.Contracts.Auctions;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions")]
public sealed class AuctionsController : ControllerBase
{
    private readonly IAuctionService _auctionService;
    private readonly ICurrentUserService _currentUserService;

    public AuctionsController(
        IAuctionService auctionService,
        ICurrentUserService currentUserService)
    {
        _auctionService = auctionService;
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
            return BadRequest("El precio mínimo no puede ser mayor al precio máximo.");

        if (!string.IsNullOrWhiteSpace(ordenar) && ordenar is not ("tiempo" or "precio"))
            return BadRequest("El orden debe ser 'tiempo' o 'precio'.");

        if (page < 1)
            return BadRequest("La página debe ser mayor o igual a 1.");

        if (pageSize is < 1 or > 50)
            return BadRequest("El tamaño de página debe estar entre 1 y 50.");

        var resultado = await _auctionService.ObtenerSubastasAsync(
            buscar, precioMin, precioMax, categoria, estado, ordenar,
            page, pageSize, cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("categories")]
    [ProducesResponseType(
        typeof(IReadOnlyList<string>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> ObtenerCategorias(
        CancellationToken cancellationToken)
    {
        return Ok(await _auctionService.ObtenerCategoriasAsync(
            cancellationToken));
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
            return BadRequest("El título es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Descripcion))
            return BadRequest("La descripción es obligatoria.");

        if (string.IsNullOrWhiteSpace(request.Categoria))
            return BadRequest("La categoría es obligatoria.");

        if (request.PrecioInicial <= 0)
            return BadRequest("El precio inicial debe ser mayor a 0.");

        if (request.IncrementoMinimo <= 0)
            return BadRequest("El incremento mínimo debe ser mayor a 0.");

        if (request.FechaFinUtc <= request.FechaInicioUtc)
            return BadRequest("La fecha de finalización debe ser posterior a la fecha de inicio.");

        if (request.FechaFinUtc <= DateTimeOffset.UtcNow)
            return BadRequest("La fecha de finalización debe ser futura.");

        var resultado = await _auctionService.CrearSubastaAsync(
            userId,
            request,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, resultado);
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

        var resultado = await _auctionService.ObtenerPublicacionesAsync(
            userId,
            cancellationToken);

        return Ok(resultado);
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
                message = "Título, descripción y categoría son obligatorios."
            });
        }

        try
        {
            return Ok(await _auctionService.ActualizarPublicacionAsync(
                subastaId,
                userId,
                request,
                cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AuctionOperationConflictException ex)
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
            await _auctionService.EliminarPublicacionAsync(
                subastaId,
                userId,
                cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AuctionOperationConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
