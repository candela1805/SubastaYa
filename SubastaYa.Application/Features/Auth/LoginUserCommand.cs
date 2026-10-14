using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Abstractions.Security;

namespace SubastaYa.Application.Features.Auth;

public sealed record LoginUserCommand(
    string Email,
    string Password);

public sealed class LoginUserHandler
    : ICommandHandler<LoginUserCommand, AuthResult?>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;

    public LoginUserHandler(
        IUserRepository userRepository,
        IPasswordService passwordService)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
    }

    public async Task<AuthResult?> HandleAsync(
        LoginUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var email = command.Email.Trim().ToLowerInvariant();

        var usuario = await _userRepository.GetByEmailAsync(
            email,
            cancellationToken);

        if (usuario is null)
            return null;

        var verificationResult = _passwordService.Verify(
            usuario,
            command.Password);

        if (verificationResult == PasswordCheckResult.Failed)
            return null;

        if (verificationResult == PasswordCheckResult.SuccessRehashNeeded)
        {
            usuario.PasswordHash = _passwordService.Hash(
                usuario,
                command.Password);

            await _userRepository.SaveChangesAsync(cancellationToken);
        }

        return new AuthResult(
            usuario.Id,
            usuario.Nombre,
            usuario.Email);
    }
}