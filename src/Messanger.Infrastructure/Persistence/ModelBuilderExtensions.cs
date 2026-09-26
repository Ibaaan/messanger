using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence;

internal static class ModelBuilderExtensions
{
    public static void UseSnakeCaseColumnNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            var columnName = Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2")
                .ToLowerInvariant();
            property.SetColumnName(columnName);
        }
    }
}