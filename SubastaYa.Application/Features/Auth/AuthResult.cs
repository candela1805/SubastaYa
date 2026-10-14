namespace SubastaYa.Application.Features.Auth;

public sealed record AuthResult(
    Guid UserId,
    string Nombre,
    string Email);