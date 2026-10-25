using Messanger.Operations;
using Messanger.Security;
using Messanger.Accounts;
using Messanger.Application.Accounts;
using Messanger.Authentication;
using Messanger.Chats;
using Messanger.Realtime;
using Messanger.Application.Chats;
using Messanger.Application.Messages;
using Messanger.Http;
using Messanger.Infrastructure;
using Messanger.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http.Json;

namespace Messanger;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = builder.Configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException(
                                   "ConnectionStrings:Postgres is required. See README.md.");

        builder.Services.AddProtectionStorage(builder.Configuration, builder.Environment.IsDevelopment());
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.MaxDepth = 32);
        builder.Services.AddPostgresRepositories(connectionString);
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<PasswordChangeService>();
        builder.Services.AddScoped<ChatAccessService>();
        builder.Services.AddScoped<ChatService>();
        builder.Services.AddScoped<ChatMembershipService>();
        builder.Services.AddScoped<ChatReadService>();
        builder.Services.AddScoped<MessageService>();
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
        builder.Services.AddOperations(builder.Configuration);
        builder.Services.AddSecurityControls(builder.Configuration);
        builder.Services.AddChatRealtime();
        builder.Services.AddAccessAuthentication(builder.Configuration);
        builder.Services.AddBrowserSessions(builder.Configuration);
        builder.Services.AddHttpSecurity(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddApiContracts();

        var app = builder.Build();
        // Fail startup on an invalid/missing signing key rather than the first login request.
        app.Services.GetRequiredService<JwtSigningKey>();
        _ = app.Services.GetRequiredService<ProtectionCertificates>().Active;
        app.UseRouting();
        app.UseMiddleware<RequestObservabilityMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages(async status =>
        {
            var (code, message) = ApiError.ForStatus(status.HttpContext.Response.StatusCode);
            await ApiError.WriteAsync(status.HttpContext, status.HttpContext.Response.StatusCode, code, message);
        });
        app.UseCoreHttpSecurity(app.Environment.IsDevelopment());
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOperations();
        app.MapAuthenticationEndpoints();
        app.MapSessionEndpoints();
        app.MapAccountEndpoints();
        app.MapChatEndpoints();
        app.MapChatMembershipEndpoints();
        if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
        app.MapChatRealtime();
        await app.RunAsync();
    }
}