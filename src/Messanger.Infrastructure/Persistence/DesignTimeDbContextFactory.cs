using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Messanger.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MessangerDbContext>
{
    public MessangerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                               ?? throw new InvalidOperationException(
                                   "Set ConnectionStrings__Postgres before running EF commands.");
        return new MessangerDbContext(new DbContextOptionsBuilder<MessangerDbContext>()
            .UseNpgsql(connectionString, pg => pg.SetPostgresVersion(18, 0)).Options);
    }
}