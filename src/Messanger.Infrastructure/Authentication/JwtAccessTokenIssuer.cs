using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Messanger.Application;
using Messanger.Application.Authentication;
using Messanger.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.Infrastructure.Authentication;

internal sealed class JwtAccessTokenIssuer(
    JwtSigningKey signingKey,
    IOptions<JwtOptions> options,
    TimeProvider clock) : IAccessTokenIssuer
{
    public AccessToken Issue(User user, Guid familyId, DateTimeOffset deadline)
    {
        var now = clock.GetUtcNow();
        if (deadline <= now) throw new UseCaseException("session_expired", "Your session expired. Please log in again.");
        var expiresAt = now.AddMinutes(15) < deadline ? now.AddMinutes(15) : deadline;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("role", user.Role),
                new Claim("sid", familyId.ToString()),
                new Claim("session_version", user.SessionVersion.ToString(CultureInfo.InvariantCulture)),
                new Claim("token_use", "access")
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.RsaSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return new AccessToken(handler.CreateEncodedJwt(descriptor), expiresAt);
    }
}