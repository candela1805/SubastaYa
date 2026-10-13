using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubastaYa.API.Contracts.Users;
using SubastaYa.API.Services;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Exceptions;

namespace SubastaYa.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        IUserRepository userRepository,
        ICurrentUserService currentUserService)
    {
        _userRepository = userRepository;
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

        var user = await _userRepository.GetByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return NotFound(new
            {
                message = "No se encontró el usuario autenticado."
            });
        }

        return Ok(new UserProfileResponse
        {
            UserId = user.Id,
            Nombre = user.Nombre,
            Email = user.Email
        });
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
        {
            return BadRequest(new
            {
                message = "El nombre es obligatorio."
            });
        }

        var user = await _userRepository.GetByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return NotFound(new
            {
                message = "No se encontró el usuario autenticado."
            });
        }

        var emailInUse = await _userRepository.EmailExistsAsync(
            email,
            userId,
            cancellationToken);

        if (emailInUse)
        {
            return Conflict(new
            {
                message = "Ya existe un usuario registrado con ese email."
            });
        }

        user.Nombre = nombre;
        user.Email = email;

        try
        {
            await _userRepository.SaveChangesAsync(cancellationToken);
        }
        catch (UserPersistenceException)
        {
            return Conflict(new
            {
                message = "No se pudo actualizar el perfil. El email podría estar registrado."
            });
        }

        return Ok(new UserProfileResponse
        {
            UserId = user.Id,
            Nombre = user.Nombre,
            Email = user.Email
        });
    }
}
