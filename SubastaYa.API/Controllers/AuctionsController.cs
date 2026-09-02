using Microsoft.AspNetCore.Mvc;
using SubastaYa.API.Contracts.Auctions;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions")]
public sealed class AuctionsController : ControllerBase
{
    private readonly IAuctionService _auctionService;

    public AuctionsController(IAuctionService auctionService)
    {
        _auctionService = auctionService;
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

    [HttpPost]
    [ProducesResponseType(typeof(AuctionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuctionResponse>> CrearSubasta(
        CreateAuctionRequest request,
        CancellationToken cancellationToken)
    {
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

        var resultado = await _auctionService.CrearSubastaAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, resultado);
    }
}
