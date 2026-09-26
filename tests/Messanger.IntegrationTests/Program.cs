using Messanger.Infrastructure;
using Messanger.IntegrationTests;
using Microsoft.Extensions.DependencyInjection;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres to a migrated test database.");

var services = new ServiceCollection().AddPostgresRepositories(connectionString);
await using var provider = services.BuildServiceProvider();

(string Name, Func<TestDatabase, Task> Run)[] tests =
[
    (nameof(UserRepositoryTests.AddPersistsUserAndProfile), UserRepositoryTests.AddPersistsUserAndProfile),
    (nameof(UserRepositoryTests.DuplicateUsernameIsRejected), UserRepositoryTests.DuplicateUsernameIsRejected),
    (nameof(ChatRepositoryTests.ActiveMemberCanFindChat), ChatRepositoryTests.ActiveMemberCanFindChat),
    (nameof(ChatRepositoryTests.DepartedMemberCannotFindChat), ChatRepositoryTests.DepartedMemberCannotFindChat),
    (nameof(MessageRepositoryTests.HistoryReturnsNewestMessagesFirst), MessageRepositoryTests.HistoryReturnsNewestMessagesFirst),
    (nameof(MessageRepositoryTests.HistoryCursorExcludesNewerMessages), MessageRepositoryTests.HistoryCursorExcludesNewerMessages),
    (nameof(RefreshTokenFamilyRepositoryTests.RevokeFamilyPersistsRevocation), RefreshTokenFamilyRepositoryTests.RevokeFamilyPersistsRevocation)
];

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        // Each scenario gets its own scope and transaction, including cleanup on failure.
        await using (var database = await TestDatabase.CreateAsync(provider))
        {
            await run(database);
        }
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} integration checks passed. Test transactions rolled back.");
return failures == 0 ? 0 : 1;
