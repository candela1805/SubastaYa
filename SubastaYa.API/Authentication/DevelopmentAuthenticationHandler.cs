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

        var configuredUserId = Options.UserId;

        if (Request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        {
            configuredUserId = headerUserId.ToString();
        }

        if (!Guid.TryParse(configuredUserId, out var userId) ||
            userId == Guid.Empty)
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "El usuario configurado para DevelopmentAuthentication no es válido."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        if (Guid.TryParse(Options.UserId, out var demoUserId) &&
            userId == demoUserId)
        {
            claims.Add(new Claim(ClaimTypes.Name, Options.Name));
            claims.Add(new Claim(ClaimTypes.Email, Options.Email));
        }
        else
        {
            // Identidad ficticia de pruebas: no atribuirle el email del demo.
            claims.Add(new Claim(
                ClaimTypes.Name,
                $"Usuario de prueba {userId}"));
        }

        var identity = new ClaimsIdentity(
            claims,
            DevelopmentAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        var ticket = new AuthenticationTicket(
            principal,
            DevelopmentAuthenticationDefaults.AuthenticationScheme);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }
}
