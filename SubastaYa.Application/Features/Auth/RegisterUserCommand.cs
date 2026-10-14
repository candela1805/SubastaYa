using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Abstractions.Security;
using SubastaYa.Application.Exceptions;
using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Features.Auth;

public sealed record RegisterUserCommand(
    string Nombre,
    string Email,
    string Password);

public sealed class RegisterUserHandler
    : ICommandHandler<RegisterUserCommand, AuthResult>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;

    public RegisterUserHandler(
        IUserRepository userRepository,
        IPasswordService passwordService)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
    }

    public async Task<AuthResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var nombre = command.Nombre.Trim();
        var email = command.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new AuthValidationException(
                "El nombre es obligatorio.");
        }

        var emailExists = await _userRepository.EmailExistsAsync(
            email,
            null,
            cancellationToken);

        if (emailExists)
        {
            throw new AuthConflictException(
                "Ya existe un usuario registrado con ese email.");
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nombre = nombre,
            Email = email,
            FechaRegistro = DateTimeOffset.UtcNow
        };

        usuario.PasswordHash = _passwordService.Hash(
            usuario,
            command.Password);

        usuario.Billetera = new Billetera
        {
            UsuarioId = usuario.Id,
            Usuario = usuario,
            SaldoTotal = 0,
            SaldoRetenido = 0,
            SaldoDisponible = 0
        };

        _userRepository.Add(usuario);

        try
        {
            await _userRepository.SaveChangesAsync(cancellationToken);
        }
        catch (UserPersistenceException ex)
        {
            throw new AuthConflictException(
                "No se pudo crear el usuario. El email podría estar registrado.",
                ex);
        }

        return new AuthResult(
            usuario.Id,
            usuario.Nombre,
            usuario.Email);
    }
}