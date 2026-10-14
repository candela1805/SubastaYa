using Microsoft.AspNetCore.Identity;
using SubastaYa.Application.Abstractions.Security;
using SubastaYa.Domain.Models;

namespace SubastaYa.Infrastructure.Security;

public sealed class IdentityPasswordService : IPasswordService
{
    private readonly IPasswordHasher<Usuario> _passwordHasher;

    public IdentityPasswordService(IPasswordHasher<Usuario> passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    public string Hash(Usuario usuario, string password)
    {
        return _passwordHasher.HashPassword(usuario, password);
    }

    public PasswordCheckResult Verify(Usuario usuario, string password)
    {
        if (string.IsNullOrWhiteSpace(usuario.PasswordHash))
            return PasswordCheckResult.Failed;

        try
        {
            var result = _passwordHasher.VerifyHashedPassword(
                usuario,
                usuario.PasswordHash,
                password);

            return result switch
            {
                PasswordVerificationResult.Success =>
                    PasswordCheckResult.Success,

                PasswordVerificationResult.SuccessRehashNeeded =>
                    PasswordCheckResult.SuccessRehashNeeded,

                _ => PasswordCheckResult.Failed
            };
        }
        catch (FormatException)
        {
            return PasswordCheckResult.Failed;
        }
    }
}