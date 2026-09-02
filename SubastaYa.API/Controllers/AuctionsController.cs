using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Data;
using SubastaYa.API.Models;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auctions")]
public sealed class AuctionsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AuctionsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AuctionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AuctionDto>>> ObtenerSubastas(
        CancellationToken cancellationToken)
    {
        var subastas = await _dbContext.Subastas
            .AsNoTracking()
            .Where(subasta => subasta.Estado == "ACTIVA")
            .OrderBy(subasta => subasta.FechaFin)
            .ToListAsync(cancellationToken);

        var subastaIds = subastas.Select(subasta => subasta.Id).ToArray();
        var pujas = await _dbContext.Pujas
            .AsNoTracking()
            .Where(puja => subastaIds.Contains(puja.SubastaId))
            .ToListAsync(cancellationToken);

        var response = subastas.Select(subasta => new AuctionDto(
            subasta.Id,
            subasta.Titulo,
            subasta.Descripcion,
            subasta.UrlImagen,
            pujas
                .Where(puja => puja.SubastaId == subasta.Id)
                .Select(puja => puja.Monto)
                .DefaultIfEmpty(subasta.PrecioBase)
                .Max(),
            subasta.FechaFin,
            subasta.Estado));

        return Ok(response);
    }
}
