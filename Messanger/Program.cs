using Messanger.Authentication;
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

        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.MaxDepth = 8);
        builder.Services.AddPostgresRepositories(connectionString);
        builder.Services.AddAccessAuthentication(builder.Configuration);
        builder.Services.AddHttpSecurity(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        // Fail startup on an invalid/missing signing key rather than the first login request.
        app.Services.GetRequiredService<JwtSigningKey>();
        app.UseExceptionHandler();
        app.UseStatusCodePages(async status =>
        {
            var (code, message) = ApiError.ForStatus(status.HttpContext.Response.StatusCode);
            await ApiError.WriteAsync(status.HttpContext, status.HttpContext.Response.StatusCode, code, message);
        });
        app.UseCoreHttpSecurity(app.Environment.IsDevelopment());
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuthenticationEndpoints();
        await app.RunAsync();
    }
}