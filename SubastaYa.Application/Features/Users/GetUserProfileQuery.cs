using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;

namespace SubastaYa.Application.Features.Users;

public sealed record GetUserProfileQuery(Guid UserId);

public sealed class GetUserProfileHandler
    : IQueryHandler<GetUserProfileQuery, UserProfileResult?>
{
    private readonly IUserRepository _userRepository;

    public GetUserProfileHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserProfileResult?> HandleAsync(
        GetUserProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(
            query.UserId,
            cancellationToken);

        return user is null
            ? null
            : new UserProfileResult(
                user.Id,
                user.Nombre,
                user.Email);
    }
}