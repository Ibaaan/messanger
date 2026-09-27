using Messanger.Domain;

namespace Messanger.Application;

public sealed record UserResponse(long Id, string Username, string Role, string Language)
{
    public static UserResponse From(User user)
    {
        return new UserResponse(user.Id, user.Username, user.Role, user.Language);
    }
}