namespace SubastaYa.API.Models;

public sealed record AuctionDto(
    int Id,
    string Title,
    string? Description,
    string? Img,
    decimal CurrentBid,
    DateTime EndTime,
    string Status);
