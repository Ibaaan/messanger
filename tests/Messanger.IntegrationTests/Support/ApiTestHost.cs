using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messanger.Authentication;
using Messanger.Application;
using Messanger.Application.Authentication;
using Messanger.Http;
using Messanger.Security;
using Messanger.Infrastructure.Authentication;
using Messanger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Messanger.IntegrationTests.Support;

internal sealed class ApiTestHost : WebApplicationFactory<Program>
{
    public const string Issuer = "https://messanger.test";
    public const string Audience = "messanger-tests";
    public const string AllowedOrigin = "https://client.example";
    public const string TestPassword = "a test passphrase 123";
    private readonly string adminConnectionString;
    private readonly string connectionString;
    private readonly string environment;
    private readonly TimeProvider? clock;
    private readonly Action<SecurityLimitsOptions>? security;
    private readonly ILoggerProvider? securityLogs;
    private readonly Action<IServiceCollection>? overrides;
    private readonly string keyDirectory;
    private readonly string schema;

    private ApiTestHost(string adminConnectionString, string environment, TimeProvider? clock, Action<SecurityLimitsOptions>? security, ILoggerProvider? securityLogs, Action<IServiceCollection>? overrides)
    {
        this.adminConnectionString = adminConnectionString;
        this.environment = environment;
        this.clock = clock;
        this.security = security;
        this.securityLogs = securityLogs;
        this.overrides = overrides;
        schema = "api_tests_" + Guid.NewGuid().ToString("N");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema }
            .ConnectionString;
        keyDirectory = Directory.CreateTempSubdirectory("messanger-api-tests-").FullName;
        File.WriteAllText(Path.Combine(keyDirectory, "signing.pem"), SigningKey.ExportPkcs8PrivateKeyPem());
    }

    public RSA SigningKey { get; } = RSA.Create(2048);
    public HttpClient Client { get; private set; } = null!;

    public static async Task<ApiTestHost> CreateAsync(string connectionString, string environment = "Development", TimeProvider? clock = null, Action<SecurityLimitsOptions>? security = null, ILoggerProvider? securityLogs = null, Action<IServiceCollection>? overrides = null)
    {
        var host = new ApiTestHost(connectionString, environment, clock, security, securityLogs, overrides);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA {host.schema}", connection);
            await command.ExecuteNonQueryAsync();
            await using var context = new MessangerDbContext(new DbContextOptionsBuilder<MessangerDbContext>()
                .UseNpgsql(host.connectionString).Options);
            await context.Database.MigrateAsync();
            host.Client = host.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
            using var csrf = await host.Client.GetAsync("/auth/csrf");
            csrf.EnsureSuccessStatusCode();
            var proof = await csrf.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            host.Client.DefaultRequestHeaders.Add("X-CSRF-Token", proof.GetProperty("token").GetString());
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureLogging(logging => { logging.ClearProviders(); if (securityLogs is not null) logging.AddProvider(securityLogs); });
        builder.ConfigureServices(services =>
        {
            // All request scopes use the isolated schema, never the caller's application tables.
            if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
            services.PostConfigure<SecurityLimitsOptions>(options =>
            {
                if (security is null) options.Enabled = false;
                else { options.Enabled = true; security(options); }
            });
            if (security is not null) services.AddSingleton<IStartupFilter, TestPeerAddress>();
            // Retention scenarios run explicit passes; normal API tests must not race a background purge.
            services.PostConfigure<RefreshTokenCleanupOptions>(options => options.Enabled = false);
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlDataSourceBuilder(connectionString) { Name = "messanger" }.Build());
            services.PostConfigure<JwtOptions>(options =>
            {
                options.Issuer = Issuer;
                options.Audience = Audience;
                options.PrivateKeyPath = Path.Combine(keyDirectory, "signing.pem");
            });
            services.PostConfigure<HttpSecurityOptions>(options => options.AllowedOrigins = [AllowedOrigin]);
            overrides?.Invoke(services);
        });
    }

    public Task<ApiTestHost> CreateSecurityHostAsync(Action<SecurityLimitsOptions> options, ILoggerProvider? logs = null, Action<IServiceCollection>? overrides = null) =>
        CreateAsync(adminConnectionString, security: options, securityLogs: logs, overrides: overrides);

    public Task<ApiTestHost> CreateClockHostAsync(TimeProvider clock) => CreateAsync(adminConnectionString, clock: clock);

    public Task<ApiTestHost> CreateProductionHostAsync()
    {
        return CreateAsync(adminConnectionString, "Production");
    }

    public async Task<UserResponse> RegisterAsync(string? username = null)
    {
        using var response = await Client.PostAsJsonAsync("/registration", new
        {
            username = username ?? NewUsername(),
            password = TestPassword
        });
        TestAssert.Equal(HttpStatusCode.Created, response.StatusCode, "Registration should succeed.");
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    public async Task<LoginResponse> LoginAsync(UserResponse user)
    {
        using var browser = await SessionBrowser.CreateAsync(this);
        using var response = await browser.Client.PostAsJsonAsync("/auth/login", new { username = user.Username, password = TestPassword });
        TestAssert.Equal(HttpStatusCode.OK, response.StatusCode, "Login should succeed.");
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public Task<HttpResponseMessage> SendAsAsync(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return SendAsync(request);
    }

    public Task<HttpResponseMessage> GetCurrentUserAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request);
        }
    }

    public async Task SetStatusAsync(long userId, string status)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var user = await context.Users.SingleAsync(user => user.Id == userId);
        user.AccountStatus = status;
        await context.SaveChangesAsync();
    }

    public static string NewUsername()
    {
        return "api_" + Guid.NewGuid().ToString("N")[..12];
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            try
            {
                await using var connection = new NpgsqlConnection(adminConnectionString);
                await connection.OpenAsync();
                // The name is generated above from a fixed prefix and GUID, never from user input.
                await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                SigningKey.Dispose();
                Directory.Delete(keyDirectory, true);
            }
        }
    }
}