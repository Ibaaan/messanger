using Messanger.Application.Persistence;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Support;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly AsyncServiceScope scope;
    private readonly IDbContextTransaction transaction;

    private TestDatabase(AsyncServiceScope scope, IDbContextTransaction transaction)
    {
        this.scope = scope;
        this.transaction = transaction;
    }

    public MessangerDbContext Context => scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
    public IUserRepository Users => scope.ServiceProvider.GetRequiredService<IUserRepository>();
    public IChatRepository Chats => scope.ServiceProvider.GetRequiredService<IChatRepository>();
    public IMessageRepository Messages => scope.ServiceProvider.GetRequiredService<IMessageRepository>();

    public IRefreshTokenFamilyRepository Tokens =>
        scope.ServiceProvider.GetRequiredService<IRefreshTokenFamilyRepository>();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await transaction.RollbackAsync();
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }

    public static async Task<TestDatabase> CreateAsync(IServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            var transaction = await context.Database.BeginTransactionAsync();
            return new TestDatabase(scope, transaction);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public Task<int> SaveChangesAsync()
    {
        return scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    public async Task<User> CreateUserAsync(string? username = null)
    {
        var user = new User
        {
            Username = username ?? "u" + Guid.NewGuid().ToString("N")[..15],
            NormalizedUsername = "",
            PasswordHash = "test-hash"
        };
        Users.Add(user, new UserProfile { DisplayName = "Test User" });
        await SaveChangesAsync();
        return user;
    }

    public async Task<Chat> CreateChatAsync()
    {
        var chat = new Chat { Name = "Test Chat" };
        Chats.Add(chat);
        await SaveChangesAsync();
        return chat;
    }

    public async Task<ChatParticipant> AddMemberAsync(Chat chat, User user)
    {
        var participant = new ChatParticipant { ChatId = chat.Id, UserId = user.Id };
        Chats.AddParticipant(participant);
        await SaveChangesAsync();
        return participant;
    }

    public async Task<Message> CreateMessageAsync(Chat chat, User sender, string text)
    {
        var message = new Message { ChatId = chat.Id, SenderId = sender.Id, Text = text };
        Messages.Add(message);
        await SaveChangesAsync();
        return message;
    }
}