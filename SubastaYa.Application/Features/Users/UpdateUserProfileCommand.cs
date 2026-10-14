using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Exceptions;

namespace SubastaYa.Application.Features.Users;

public sealed record UpdateUserProfileCommand(
    Guid UserId,
    string Nombre,
    string Email);

public sealed class UpdateUserProfileHandler
    : ICommandHandler<UpdateUserProfileCommand, UserProfileResult?>
{
    private readonly IUserRepository _userRepository;

    public UpdateUserProfileHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserProfileResult?> HandleAsync(
        UpdateUserProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var nombre = command.Nombre.Trim();
        var email = command.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new UserProfileValidationException(
                "El nombre es obligatorio.");
        }

        var user = await _userRepository.GetByIdAsync(
            command.UserId,
            cancellationToken);

        if (user is null)
            return null;

        var emailInUse = await _userRepository.EmailExistsAsync(
            email,
            command.UserId,
            cancellationToken);

        if (emailInUse)
        {
            throw new UserProfileConflictException(
                "Ya existe un usuario registrado con ese email.");
        }

        user.Nombre = nombre;
        user.Email = email;

        try
        {
            await _userRepository.SaveChangesAsync(cancellationToken);
        }
        catch (UserPersistenceException ex)
        {
            throw new UserProfileConflictException(
                "No se pudo actualizar el perfil. El email podría estar registrado.",
                ex);
        }

        return new UserProfileResult(
            user.Id,
            user.Nombre,
            user.Email);
    }
}