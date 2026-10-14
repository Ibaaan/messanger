using Messanger.Application.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.Infrastructure.Authentication;

public static class AuthenticationDependencyInjection
{
    public static IServiceCollection AddAuthenticationInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.PrivateKeyPath), "Jwt:PrivateKeyPath is required.")
            .ValidateOnStart();
        services.AddSingleton<JwtSigningKey>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IRefreshTokenCodec, JwtRefreshTokenCodec>();
        services.AddSingleton<IPasswordService, IdentityPasswordService>();
        return services;
    }
}