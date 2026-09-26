using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence;

public sealed class MessangerDbContext(DbContextOptions<MessangerDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatParticipant> Participants => Set<ChatParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<RefreshTokenFamily> RefreshTokens => Set<RefreshTokenFamily>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessangerDbContext).Assembly);
        modelBuilder.UseSnakeCaseColumnNames();
    }
}