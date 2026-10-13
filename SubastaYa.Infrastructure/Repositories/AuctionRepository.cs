using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;

namespace SubastaYa.Infrastructure.Repositories;

public sealed class AuctionRepository : IAuctionRepository
{
    private readonly ApplicationDbContext _dbContext;
    public AuctionRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<PagedAuctionResult> SearchAsync(string? search, decimal? minPrice, decimal? maxPrice, string? category, EstadoSubasta? state, string? order, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.Subastas.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) { var text = search.Trim(); query = query.Where(x => x.Titulo.Contains(text)); }
        if (minPrice.HasValue) query = query.Where(x => x.PrecioActual >= minPrice.Value);
        if (maxPrice.HasValue) query = query.Where(x => x.PrecioActual <= maxPrice.Value);
        if (!string.IsNullOrWhiteSpace(category)) { var value = category.Trim().ToUpper(); query = query.Where(x => x.Categoria.Trim().ToUpper() == value); }
        if (state.HasValue) query = query.Where(x => x.Estado == state.Value);
        var total = await query.CountAsync(cancellationToken);
        query = order == "precio" ? query.OrderByDescending(x => x.PrecioActual).ThenBy(x => x.FechaFinUtc) : query.OrderBy(x => x.FechaFinUtc).ThenBy(x => x.Id);
        var items = await Project(query.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var values = await _dbContext.Subastas.AsNoTracking().Select(x => x.Categoria).ToListAsync(cancellationToken);
        return values.Select(x => x.Trim()).Where(x => x.Length > 0).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(x => x, StringComparer.Ordinal).First()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<AuctionResult>> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken) =>
        await Project(_dbContext.Subastas.AsNoTracking().Where(x => x.VendedorId == sellerId).OrderByDescending(x => x.FechaInicioUtc).ThenByDescending(x => x.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BidActivityResult>> GetBidActivitiesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ownBids = _dbContext.Pujas.AsNoTracking().Where(x => x.UsuarioId == userId).GroupBy(x => x.SubastaId).Select(g => new { SubastaId = g.Key, MyBid = g.Max(x => x.Monto) });
        return await (from bid in ownBids join auction in _dbContext.Subastas.AsNoTracking() on bid.SubastaId equals auction.Id join settlement in _dbContext.LiquidacionesSubasta.AsNoTracking() on auction.Id equals settlement.SubastaId into settlements from settlement in settlements.DefaultIfEmpty() orderby auction.FechaFinUtc descending, auction.Id select new BidActivityResult(auction.Id, auction.Titulo, auction.Descripcion ?? string.Empty, auction.ImagenUrl, auction.Categoria, auction.PrecioInicial, bid.MyBid, auction.PrecioActual, auction.IncrementoMinimo, auction.FechaInicioUtc, auction.FechaFinUtc, auction.Estado, auction.Estado == EstadoSubasta.Programada || auction.Estado == EstadoSubasta.Activa ? "EnCurso" : settlement != null && settlement.CompradorId == userId ? "Ganada" : "Perdida")).ToListAsync(cancellationToken);
    }

    public Task<Subasta?> GetOwnedTrackedAsync(Guid auctionId, Guid sellerId, CancellationToken cancellationToken) => _dbContext.Subastas.SingleOrDefaultAsync(x => x.Id == auctionId && x.VendedorId == sellerId, cancellationToken);
    public Task<Subasta?> GetTrackedAsync(Guid auctionId, CancellationToken cancellationToken) => _dbContext.Subastas.SingleOrDefaultAsync(x => x.Id == auctionId, cancellationToken);
    public Task<bool> HasBidsAsync(Guid id, CancellationToken ct) => _dbContext.Pujas.AnyAsync(x => x.SubastaId == id, ct);
    public Task<bool> HasSettlementAsync(Guid id, CancellationToken ct) => _dbContext.LiquidacionesSubasta.AnyAsync(x => x.SubastaId == id, ct);
    public Task<bool> HasAuditAsync(Guid id, CancellationToken ct) => _dbContext.AuditoriaLogs.AnyAsync(x => x.SubastaId == id, ct);
    public async Task<IReadOnlyList<Guid>> GetScheduledIdsAsync(DateTimeOffset now, int size, CancellationToken ct) => await _dbContext.Subastas.AsNoTracking().Where(x => x.Estado == EstadoSubasta.Programada && x.FechaInicioUtc <= now).OrderBy(x => x.FechaInicioUtc).ThenBy(x => x.Id).Select(x => x.Id).Take(size).ToListAsync(ct);
    public async Task<IReadOnlyList<Guid>> GetExpiredIdsAsync(DateTimeOffset now, int size, CancellationToken ct) => await _dbContext.Subastas.AsNoTracking().Where(x => x.Estado == EstadoSubasta.Activa && x.FechaFinUtc <= now).OrderBy(x => x.FechaFinUtc).ThenBy(x => x.Id).Select(x => x.Id).Take(size).ToListAsync(ct);
    public void Add(Subasta auction) => _dbContext.Subastas.Add(auction);
    public void Remove(Subasta auction) => _dbContext.Subastas.Remove(auction);
    public void AddAudit(AuditoriaLog audit) => _dbContext.AuditoriaLogs.Add(audit);
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException ex) { throw new AuctionPersistenceConcurrencyException(ex); }
        catch (DbUpdateException ex) { throw new AuctionPersistenceException(ex); }
    }

    private static IQueryable<AuctionResult> Project(IQueryable<Subasta> query) => query.Select(x => new AuctionResult(x.Id, x.Titulo, x.Descripcion ?? string.Empty, x.ImagenUrl, x.Categoria, x.PrecioInicial, x.PrecioActual, x.IncrementoMinimo, x.FechaInicioUtc, x.FechaFinUtc, x.Estado));
}
