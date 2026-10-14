using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubastaYa.API.Contracts.Auth;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Auth;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ICommandHandler<
        RegisterUserCommand, AuthResult> _registerHandler;

    private readonly ICommandHandler<
        LoginUserCommand, AuthResult?> _loginHandler;

    public AuthController(
        ICommandHandler<RegisterUserCommand, AuthResult> registerHandler,
        ICommandHandler<LoginUserCommand, AuthResult?> loginHandler)
    {
        _registerHandler = registerHandler;
        _loginHandler = loginHandler;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _registerHandler.HandleAsync(
                new RegisterUserCommand(
                    request.Nombre,
                    request.Email,
                    request.Password),
                cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                MapearRespuesta(result));
        }
        catch (AuthValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AuthConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _loginHandler.HandleAsync(
            new LoginUserCommand(
                request.Email,
                request.Password),
            cancellationToken);

        if (result is null)
        {
            return Unauthorized(new
            {
                message = "Email o contraseña incorrectos."
            });
        }

        return Ok(MapearRespuesta(result));
    }

    private static AuthResponse MapearRespuesta(AuthResult result)
    {
        return new AuthResponse
        {
            UserId = result.UserId,
            Nombre = result.Nombre,
            Email = result.Email
        };
    }
}