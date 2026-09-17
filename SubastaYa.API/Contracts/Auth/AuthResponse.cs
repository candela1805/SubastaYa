namespace SubastaYa.API.Contracts.Auth;

public sealed class AuthResponse
{
    public Guid UserId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}