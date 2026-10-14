using SubastaYa.Domain.Models;

namespace SubastaYa.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<Usuario?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Usuario?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(
        string email,
        Guid? excludedUserId,
        CancellationToken cancellationToken = default);

    void Add(Usuario usuario);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}