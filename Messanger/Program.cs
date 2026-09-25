using Messanger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ConnectionStrings__Postgres. See README.md for local setup.");
    return 1;
}

var services = new ServiceCollection();
services.AddPostgresRepositories(connectionString);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
if (!await db.Database.CanConnectAsync())
{
    Console.Error.WriteLine("Could not connect to PostgreSQL.");
    return 1;
}

Console.WriteLine("Connected to PostgreSQL.");
return 0;