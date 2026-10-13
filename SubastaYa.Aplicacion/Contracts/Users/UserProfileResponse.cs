namespace SubastaYa.Aplicacion.Contracts.Users;

public sealed class UserProfileResponse
{
    public Guid UserId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
