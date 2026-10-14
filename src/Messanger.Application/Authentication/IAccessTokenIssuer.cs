using Messanger.Domain;

namespace Messanger.Application.Authentication;

public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, Guid familyId, DateTimeOffset deadline);
}