using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Auth;
using SubastaYa.API.Data;
using SubastaYa.API.Models;

namespace SubastaYa.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher<Usuario> _passwordHasher;

    public AuthController(
        ApplicationDbContext dbContext,
        IPasswordHasher<Usuario> passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var nombre = request.Nombre.Trim();

        if (string.IsNullOrWhiteSpace(nombre))
        {
            return BadRequest(new
            {
                message = "El nombre es obligatorio."
            });
        }

        var emailExists = await _dbContext.Usuarios
            .AnyAsync(
                usuario => usuario.Email == email,
                cancellationToken);

        if (emailExists)
        {
            return Conflict(new
            {
                message = "Ya existe un usuario registrado con ese email."
            });
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nombre = nombre,
            Email = email,
            FechaRegistro = DateTimeOffset.UtcNow
        };

        usuario.PasswordHash =
            _passwordHasher.HashPassword(
                usuario,
                request.Password);

        usuario.Billetera = new Billetera
        {
            UsuarioId = usuario.Id,
            Usuario = usuario,
            SaldoTotal = 0,
            SaldoRetenido = 0,
            SaldoDisponible = 0
        };

        _dbContext.Usuarios.Add(usuario);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                message = "No se pudo crear el usuario. El email podría estar registrado."
            });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new AuthResponse
            {
                UserId = usuario.Id,
                Nombre = usuario.Nombre,
                Email = usuario.Email
            });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var usuario = await _dbContext.Usuarios
            .SingleOrDefaultAsync(
                usuario => usuario.Email == email,
                cancellationToken);

        if (usuario is null)
        {
            return Unauthorized(new
            {
                message = "Email o contraseña incorrectos."
            });
        }

        PasswordVerificationResult verificationResult;

        try
        {
            verificationResult =
                _passwordHasher.VerifyHashedPassword(
                    usuario,
                    usuario.PasswordHash,
                    request.Password);
        }
        catch (FormatException)
        {
            return Unauthorized(new
            {
                message = "Email o contraseña incorrectos."
            });
        }

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new
            {
                message = "Email o contraseña incorrectos."
            });
        }

        if (verificationResult ==
            PasswordVerificationResult.SuccessRehashNeeded)
        {
            usuario.PasswordHash =
                _passwordHasher.HashPassword(
                    usuario,
                    request.Password);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new AuthResponse
        {
            UserId = usuario.Id,
            Nombre = usuario.Nombre,
            Email = usuario.Email
        });
    }
}