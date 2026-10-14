using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Security;

public enum PasswordCheckResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}

public interface IPasswordService
{
    string Hash(Usuario usuario, string password);

    PasswordCheckResult Verify(Usuario usuario, string password);
}