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
    [ProducesResponseType(typeof(IEnumerable<Subasta>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Subasta>>> ObtenerSubastas(
        CancellationToken cancellationToken)
    {
        var subastas = await _dbContext.Subastas
            .AsNoTracking()
            .OrderBy(subasta => subasta.FechaFinUtc)
            .ToListAsync(cancellationToken);

        return Ok(subastas);
    }
}