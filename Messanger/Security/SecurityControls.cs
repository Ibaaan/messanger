using System.Globalization;
using System.Threading.RateLimiting;
using Messanger.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Messanger.Security;

internal static class SecurityControls
{
    public static IServiceCollection AddSecurityControls(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecurityLimitsOptions>().Bind(configuration.GetSection("SecurityLimits"))
            .Validate(options => options.Window >= TimeSpan.FromSeconds(1) && options.Window <= TimeSpan.FromHours(1), "SecurityLimits:Window must be one second through one hour.")
            .Validate(options => new[] { options.RequestsPerServer, options.RequestsPerIp, options.RegistrationPerIp, options.LoginPerIp,
                options.LoginPerAccount, options.SessionsPerIp, options.PasswordChangesPerUser, options.CredentialConcurrency,
                options.HubCallsPerUser, options.MessagesPerUser, options.ConnectionAttemptsPerUser, options.MaximumConnectionsPerUser,
                options.MaximumConnections }.All(value => value is >= 1 and <= 1000000), "Security limits must be between 1 and 1000000.")
            .Validate(options => options.MaximumConnectionsPerUser <= options.MaximumConnections, "Per-user connection quota cannot exceed the server quota.")
            .ValidateOnStart();
        services.AddSingleton<SecurityBudgets>();
        services.AddSingleton<SecurityAudit>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<SecurityBudgets, IOptions<SecurityLimitsOptions>>((options, budgets, settings) =>
        {
            options.GlobalLimiter = budgets.Http;
            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, _) => { await RejectAsync(context.HttpContext, context.Lease, "http"); };
            options.AddPolicy("registration", context => budgets.HttpPartition(context, settings.Value.RegistrationPerIp));
            options.AddPolicy("login", context => budgets.HttpPartition(context, settings.Value.LoginPerIp));
            options.AddPolicy("sessions", context => budgets.HttpPartition(context, settings.Value.SessionsPerIp));
        });
        return services;
    }

    public static async Task<IResult> RejectAsync(HttpContext context, RateLimitLease lease, string budget)
    {
        var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var duration) ? Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)) : 1;
        context.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
        context.RequestServices.GetRequiredService<SecurityAudit>().RateLimited(context, budget);
        await ApiError.WriteAsync(context, 429, "rate_limited", "Too many requests. Try again later.");
        return Results.Empty;
    }
}
