using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SubastaYa.API.Authentication;

public sealed class DevelopmentAuthenticationHandler
    : AuthenticationHandler<DevelopmentAuthenticationOptions>
{
    private readonly IHostEnvironment _environment;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<DevelopmentAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _environment = environment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_environment.IsDevelopment() || !Options.Enabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(Options.UserId, out var userId))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "El usuario configurado para DevelopmentAuthentication no es válido."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, Options.Name),
            new Claim(ClaimTypes.Email, Options.Email)
        };

        var identity = new ClaimsIdentity(
            claims,
            DevelopmentAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            DevelopmentAuthenticationDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
