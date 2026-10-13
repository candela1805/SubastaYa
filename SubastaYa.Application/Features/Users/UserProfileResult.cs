namespace SubastaYa.Application.Features.Users;

public sealed record UserProfileResult(
    Guid UserId,
    string Nombre,
    string Email);