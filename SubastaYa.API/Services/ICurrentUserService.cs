namespace SubastaYa.API.Services;

public interface ICurrentUserService
{
    bool TryGetUserId(out Guid userId);
}
