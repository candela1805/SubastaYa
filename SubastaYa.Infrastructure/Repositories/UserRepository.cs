using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Exceptions;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Data;

namespace SubastaYa.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Usuario?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Usuarios.SingleOrDefaultAsync(
            usuario => usuario.Id == userId,
            cancellationToken);
    }

    public Task<Usuario?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Usuarios.SingleOrDefaultAsync(
            usuario => usuario.Email == email,
            cancellationToken);
    }

    public Task<bool> EmailExistsAsync(
        string email,
        Guid? excludedUserId,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Usuarios
            .Where(usuario => usuario.Email == email);

        if (excludedUserId.HasValue)
        {
            var id = excludedUserId.Value;
            query = query.Where(usuario => usuario.Id != id);
        }

        return query.AnyAsync(cancellationToken);
    }

    public void Add(Usuario usuario)
    {
        _dbContext.Usuarios.Add(usuario);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new UserPersistenceException(
                "No se pudieron guardar los cambios del usuario.",
                ex);
        }
    }
}