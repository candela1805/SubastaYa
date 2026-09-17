using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Users;
using SubastaYa.API.Data;
using SubastaYa.API.Services;

namespace SubastaYa.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        ApplicationDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> ObtenerPerfil(
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
            return Unauthorized();

        var profile = await _dbContext.Usuarios
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserProfileResponse
            {
                UserId = user.Id,
                Nombre = user.Nombre,
                Email = user.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        return profile is null
            ? NotFound(new { message = "No se encontró el usuario autenticado." })
            : Ok(profile);
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserProfileResponse>> ActualizarPerfil(
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.TryGetUserId(out var userId))
            return Unauthorized();

        var nombre = request.Nombre.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(nombre))
            return BadRequest(new { message = "El nombre es obligatorio." });

        var user = await _dbContext.Usuarios.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);

        if (user is null)
            return NotFound(new { message = "No se encontró el usuario autenticado." });

        var emailInUse = await _dbContext.Usuarios.AnyAsync(
            item => item.Id != userId && item.Email == email,
            cancellationToken);

        if (emailInUse)
            return Conflict(new { message = "Ya existe un usuario registrado con ese email." });

        user.Nombre = nombre;
        user.Email = email;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "No se pudo actualizar el perfil. El email podría estar registrado." });
        }

        return Ok(new UserProfileResponse
        {
            UserId = user.Id,
            Nombre = user.Nombre,
            Email = user.Email
        });
    }
}
