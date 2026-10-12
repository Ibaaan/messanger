using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

internal static class RealtimeRegistration
{
    public static IServiceCollection AddChatRealtime(this IServiceCollection services)
    {
        services.AddSingleton<ChatSubscriptions>();
        services.AddSingleton<AccountConnections>();
        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
        services.AddScoped<ChatBroadcaster>();
        services.AddSignalR(options =>
        {
            options.AddFilter<ChatHubFilter>();
            options.MaximumReceiveMessageSize = 16 * 1024;
            options.MaximumParallelInvocationsPerClient = 1;
            options.EnableDetailedErrors = false;
        });
        return services;
    }

    public static IEndpointRouteBuilder MapChatRealtime(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization();
        return endpoints;
    }
}
