using Messanger.Application.Persistence;
using Messanger.Infrastructure.Persistence;
using Messanger.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Messanger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPostgresRepositories(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<MessangerDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), pg => pg.SetPostgresVersion(18, 0)));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<MessangerDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IChatMembershipRepository, ChatMembershipRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IRefreshTokenFamilyRepository, RefreshTokenFamilyRepository>();
        return services;
    }
}