using Messanger.Application;
using Messanger.Domain;
using Messanger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                 ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres to a migrated test database.");
var services = new ServiceCollection().AddPostgresRepositories(connection);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
await using var transaction = await db.Database.BeginTransactionAsync();
var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
var chats = scope.ServiceProvider.GetRequiredService<IChatRepository>();
var messages = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenFamilyRepository>();
var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
var username = "u" + Guid.NewGuid().ToString("N")[..15];
var user = new User { Username = username, NormalizedUsername = "", PasswordHash = "test-hash" };
users.Add(user, new UserProfile { DisplayName = "Test" });
await unit.SaveChangesAsync();
db.ChangeTracker.Clear();
Assert((await users.GetByUsernameAsync(username.ToUpperInvariant()))?.Id == user.Id, "Normalized user lookup");
Assert(await db.Set<UserProfile>().AnyAsync(x => x.UserId == user.Id), "Profile FK fixup");
var chat = new Chat { Name = "Test" };
chats.Add(chat);
await unit.SaveChangesAsync();
var participant = new ChatParticipant { ChatId = chat.Id, UserId = user.Id };
chats.AddParticipant(participant);
await unit.SaveChangesAsync();
Assert(await chats.IsActiveParticipantAsync(chat.Id, user.Id), "Active membership");
Assert((await chats.GetForUserAsync(user.Id)).Count == 1, "User chats");
var first = new Message { ChatId = chat.Id, SenderId = user.Id, Text = "First" };
messages.Add(first);
await unit.SaveChangesAsync();
var second = new Message { ChatId = chat.Id, SenderId = user.Id, Text = "Second" };
messages.Add(second);
await unit.SaveChangesAsync();
Assert((await messages.GetHistoryAsync(chat.Id, limit: 1)).Single().Id == second.Id, "Descending bounded history");
Assert((await messages.GetHistoryAsync(chat.Id, second.Id)).Single().Id == first.Id, "History cursor");
participant.LeftAt = DateTimeOffset.UtcNow;
await unit.SaveChangesAsync();
Assert(!await chats.IsActiveParticipantAsync(chat.Id, user.Id), "Left membership excluded");
Assert((await chats.GetForUserAsync(user.Id)).Count == 0, "Left chat excluded");
var token = new RefreshTokenFamily
{
    UserId = user.Id, FamilyId = Guid.NewGuid(), JtiHash = new string('a', 64),
    ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
};
tokens.Add(token);
await unit.SaveChangesAsync();
await tokens.RevokeFamilyAsync(token.FamilyId, DateTimeOffset.UtcNow);
await unit.SaveChangesAsync();
db.ChangeTracker.Clear();
Assert((await tokens.GetByHashAsync(token.JtiHash))?.RevokedAt != null, "Family revocation persisted");
// EF creates a savepoint, allowing the surrounding test transaction to be rolled back.
users.Add(new User { Username = username, NormalizedUsername = "", PasswordHash = "test" },
    new UserProfile { DisplayName = "Duplicate" });
try
{
    await unit.SaveChangesAsync();
    throw new Exception("Unique normalized username constraint was not enforced.");
}
catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
{
}

await transaction.RollbackAsync();
Console.WriteLine("PostgreSQL integration checks passed (test transaction rolled back).");

static void Assert(bool condition, string check)
{
    if (!condition) throw new Exception($"Failed: {check}");
}