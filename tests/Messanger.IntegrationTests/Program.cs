using Messanger.Infrastructure;
using Messanger.IntegrationTests.Api;
using Messanger.IntegrationTests.Repositories;
using Messanger.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                       ?? throw new InvalidOperationException(
                           "Set ConnectionStrings__Postgres to a migrated test database.");

var services = new ServiceCollection().AddPostgresRepositories(connectionString);
await using var provider = services.BuildServiceProvider();

(string Name, Func<TestDatabase, Task> Run)[] tests =
[
    (nameof(UserRepositoryTests.AddPersistsUserAndProfile), UserRepositoryTests.AddPersistsUserAndProfile),
    (nameof(UserRepositoryTests.DuplicateUsernameIsRejected), UserRepositoryTests.DuplicateUsernameIsRejected),
    (nameof(ChatRepositoryTests.ActiveMemberCanFindChat), ChatRepositoryTests.ActiveMemberCanFindChat),
    (nameof(ChatRepositoryTests.DepartedMemberCannotFindChat), ChatRepositoryTests.DepartedMemberCannotFindChat),
    (nameof(MessageRepositoryTests.HistoryReturnsNewestMessagesFirst),
        MessageRepositoryTests.HistoryReturnsNewestMessagesFirst),
    (nameof(MessageRepositoryTests.HistoryCursorExcludesNewerMessages),
        MessageRepositoryTests.HistoryCursorExcludesNewerMessages),
    (nameof(RefreshTokenFamilyRepositoryTests.RevokeFamilyPersistsRevocation),
        RefreshTokenFamilyRepositoryTests.RevokeFamilyPersistsRevocation)
];

var failures = 0;
foreach (var (name, run) in tests)
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

(string Name, Func<ApiTestHost, Task> Run)[] apiTests =
[
    (nameof(ChatRealtimeTests.MembersReceiveMessagesAfterPersistence), ChatRealtimeTests.MembersReceiveMessagesAfterPersistence),
    (nameof(ChatRealtimeTests.OutsidersCannotJoinOrSend), ChatRealtimeTests.OutsidersCannotJoinOrSend),
    (nameof(ChatRealtimeTests.DepartedSubscribersLoseDeliveryAndSendAccess), ChatRealtimeTests.DepartedSubscribersLoseDeliveryAndSendAccess),
    (nameof(ChatRealtimeTests.ReconnectedMemberCanRecoverMissedHistory), ChatRealtimeTests.ReconnectedMemberCanRecoverMissedHistory),
    (nameof(ChatRealtimeTests.NewChatNotifiesItsMembers), ChatRealtimeTests.NewChatNotifiesItsMembers),
    (nameof(ChatRealtimeTests.HubNegotiationRequiresAuthentication), ChatRealtimeTests.HubNegotiationRequiresAuthentication),
    (nameof(ChatApiTests.SearchExcludesCallerAndInactiveUsers), ChatApiTests.SearchExcludesCallerAndInactiveUsers),
    (nameof(ChatApiTests.GroupCreationAddsOwnerAndUniqueMembers), ChatApiTests.GroupCreationAddsOwnerAndUniqueMembers),
    (nameof(ChatApiTests.InvalidParticipantsDoNotLeavePartialChats), ChatApiTests.InvalidParticipantsDoNotLeavePartialChats),
    (nameof(ChatApiTests.HistoryIsChronologicalAndLimitedToFifty), ChatApiTests.HistoryIsChronologicalAndLimitedToFifty),
    (nameof(ChatApiTests.DepartedMembersCannotReadOrSend), ChatApiTests.DepartedMembersCannotReadOrSend),
    (nameof(ChatApiTests.RepliesCannotReferenceAnotherChat), ChatApiTests.RepliesCannotReferenceAnotherChat),
    (nameof(RegistrationApiTests.CreatesHashedUserAndDefaultProfile),
        RegistrationApiTests.CreatesHashedUserAndDefaultProfile),
    (nameof(RegistrationApiTests.DuplicateNamesAreCaseInsensitive),
        RegistrationApiTests.DuplicateNamesAreCaseInsensitive),
    (nameof(RegistrationApiTests.ConcurrentRegistrationsCannotDuplicateAccounts),
        RegistrationApiTests.ConcurrentRegistrationsCannotDuplicateAccounts),
    (nameof(RegistrationApiTests.RejectsInvalidCredentialsWithoutCreatingAccounts),
        RegistrationApiTests.RejectsInvalidCredentialsWithoutCreatingAccounts),
    (nameof(LoginApiTests.AccessTokenAuthenticatesOnlyItsOwner), LoginApiTests.AccessTokenAuthenticatesOnlyItsOwner),
    (nameof(LoginApiTests.InvalidAndInactiveAccountsReturnGenericErrors),
        LoginApiTests.InvalidAndInactiveAccountsReturnGenericErrors),
    (nameof(LoginApiTests.SuspendedUserCannotUsePreviouslyIssuedToken),
        LoginApiTests.SuspendedUserCannotUsePreviouslyIssuedToken),
    (nameof(AccessTokenApiTests.MissingAndMalformedTokensAreRejected),
        AccessTokenApiTests.MissingAndMalformedTokensAreRejected),
    (nameof(AccessTokenApiTests.InvalidTokenClaimsAndSignaturesAreRejected),
        AccessTokenApiTests.InvalidTokenClaimsAndSignaturesAreRejected),
    (nameof(HttpPolicyApiTests.BrowserOriginMustMatchAllowlistExactly),
        HttpPolicyApiTests.BrowserOriginMustMatchAllowlistExactly),
    (nameof(HttpPolicyApiTests.ProductionRequiresHttps), HttpPolicyApiTests.ProductionRequiresHttps),
    (nameof(HttpPolicyApiTests.MalformedAndOversizedBodiesUseErrorEnvelope),
        HttpPolicyApiTests.MalformedAndOversizedBodiesUseErrorEnvelope)
];

// HTTP scenarios use their own generated schema; disposing the host removes all their data.
await using (var host = await ApiTestHost.CreateAsync(connectionString))
{
    foreach (var (name, run) in apiTests)
        try
        {
            await run(host);
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine($"FAIL {name}: {exception}");
        }
}

var total = tests.Length + apiTests.Length;
Console.WriteLine(
    $"{total - failures}/{total} integration checks passed. Repository transactions rolled back; API test schema removed.");
return failures == 0 ? 0 : 1;