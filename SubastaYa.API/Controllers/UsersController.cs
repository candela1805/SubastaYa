using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubastaYa.API.Contracts.Users;
using SubastaYa.API.Services;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Users;

namespace SubastaYa.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IQueryHandler<
        GetUserProfileQuery, UserProfileResult?> _getProfileHandler;

    private readonly ICommandHandler<
        UpdateUserProfileCommand, UserProfileResult?> _updateProfileHandler;

    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        IQueryHandler<GetUserProfileQuery, UserProfileResult?> getProfileHandler,
        ICommandHandler<UpdateUserProfileCommand, UserProfileResult?> updateProfileHandler,
        ICurrentUserService currentUserService)
    {
        _getProfileHandler = getProfileHandler;
        _updateProfileHandler = updateProfileHandler;
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

        var result = await _getProfileHandler.HandleAsync(
            new GetUserProfileQuery(userId),
            cancellationToken);

        if (result is null)
        {
            return NotFound(new
            {
                message = "No se encontró el usuario autenticado."
            });
        }

        return Ok(MapearRespuesta(result));
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

        try
        {
            var result = await _updateProfileHandler.HandleAsync(
                new UpdateUserProfileCommand(
                    userId,
                    request.Nombre,
                    request.Email),
                cancellationToken);

            if (result is null)
            {
                return NotFound(new
                {
                    message = "No se encontró el usuario autenticado."
                });
            }

            return Ok(MapearRespuesta(result));
        }
        catch (UserProfileValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UserProfileConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private static UserProfileResponse MapearRespuesta(
        UserProfileResult result)
    {
        return new UserProfileResponse
        {
            UserId = result.UserId,
            Nombre = result.Nombre,
            Email = result.Email
        };
    }
}