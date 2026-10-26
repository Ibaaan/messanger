using System.Net;
using System.Net.Sockets;
using Messanger.Infrastructure;
using Messanger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messanger.Operations;

internal static class OperationalCommands
{
    public static async Task<int> MigrateAsync(string connection, IConfiguration configuration)
    {
        await using var services = new ServiceCollection().AddPostgresRepositories(connection).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        await db.Database.MigrateAsync();
        if (configuration["Database:RuntimeRole"] is { Length: > 0 } role)
        {
            var identifier = new NpgsqlCommandBuilder().QuoteIdentifier(role);
            await db.Database.OpenConnectionAsync();
            await using var revoke = db.Database.GetDbConnection().CreateCommand();
            // Identifiers cannot be SQL parameters; QuoteIdentifier escapes the configured role name.
            revoke.CommandText = "REVOKE INSERT, UPDATE, DELETE ON \"__EFMigrationsHistory\" FROM " + identifier;
            await revoke.ExecuteNonQueryAsync();
        }
        return 0;
    }
    public static async Task<int> HealthAsync(IConfiguration configuration)
    {
        try
        {
            var uri = new Uri(configuration["HealthCheck:Url"] ?? "https://localhost:8443/health/ready");
            if (uri.Scheme != "https") return 1;
            using var handler = new SocketsHttpHandler { ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, uri.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            } };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(uri);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch { return 1; }
    }
}
