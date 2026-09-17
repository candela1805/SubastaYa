using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Contracts.Users;

public sealed class UpdateUserProfileRequest
{
    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;
}
