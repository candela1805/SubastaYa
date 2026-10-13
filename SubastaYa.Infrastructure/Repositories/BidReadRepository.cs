using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;

namespace SubastaYa.Infrastructure.Repositories;

public sealed class BidReadRepository : IBidReadRepository
{
    private readonly ApplicationDbContext _context;

    public BidReadRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<bool> AuctionExistsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return _context.Subastas
            .AsNoTracking()
            .AnyAsync(
                subasta => subasta.Id == subastaId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<BidHistoryResult>> GetHistoryAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        var pujas = await _context.Pujas
            .AsNoTracking()
            .Where(puja => puja.SubastaId == subastaId)
            .Select(puja => new
            {
                puja.Id,
                puja.Monto,
                puja.UsuarioId,
                puja.FechaUtc
            })
            .ToListAsync(cancellationToken);

        return pujas
            .OrderBy(puja => puja.FechaUtc)
            .ThenBy(puja => puja.Id)
            .Select(puja => new BidHistoryResult
            {
                Id = puja.Id,
                Monto = puja.Monto,
                Pseudonimo = puja.UsuarioId.ToString()[..8],
                FechaUtc = puja.FechaUtc
            })
            .ToList();
    }

    public Task<Subasta?> GetAuctionAsync(
    Guid subastaId,
    CancellationToken cancellationToken = default)
    {
        return _context.Subastas
            .AsNoTracking()
            .SingleOrDefaultAsync(
                subasta => subasta.Id == subastaId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetWinningBidUserIdsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Pujas
            .AsNoTracking()
            .Where(puja =>
                puja.SubastaId == subastaId && puja.EsGanadora)
            .Select(puja => puja.UsuarioId)
            .Take(2)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasAnyBidAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
    {
        return _context.Pujas
            .AsNoTracking()
            .AnyAsync(
                puja => puja.SubastaId == subastaId,
                cancellationToken);
    }

    public Task<bool> HasUserBidAsync(
        Guid subastaId,
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        return _context.Pujas
            .AsNoTracking()
            .AnyAsync(
                puja =>
                    puja.SubastaId == subastaId &&
                    puja.UsuarioId == usuarioId,
                cancellationToken);
    }
}
