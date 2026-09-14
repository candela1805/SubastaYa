using Microsoft.AspNetCore.Authentication;

namespace SubastaYa.API.Authentication;

public sealed class DevelopmentAuthenticationOptions : AuthenticationSchemeOptions
{
    public bool Enabled { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = "Usuario Demo";

    public string Email { get; set; } = "demo@subastaya.com";
}
