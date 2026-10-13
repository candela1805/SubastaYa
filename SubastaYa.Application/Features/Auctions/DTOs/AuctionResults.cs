using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Features.Auctions.DTOs;

public sealed record AuctionResult(
    Guid Id,
    string Titulo,
    string Descripcion,
    string? ImagenUrl,
    string Categoria,
    decimal PrecioInicial,
    decimal PrecioActual,
    decimal IncrementoMinimo,
    DateTimeOffset FechaInicioUtc,
    DateTimeOffset FechaFinUtc,
    EstadoSubasta Estado);

public sealed record PagedAuctionResult(
    IReadOnlyList<AuctionResult> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record BidActivityResult(
    Guid SubastaId,
    string Titulo,
    string Descripcion,
    string? ImagenUrl,
    string Categoria,
    decimal PrecioInicial,
    decimal MiOferta,
    decimal OfertaActual,
    decimal IncrementoMinimo,
    DateTimeOffset FechaInicioUtc,
    DateTimeOffset FechaFinUtc,
    EstadoSubasta Estado,
    string Resultado);
