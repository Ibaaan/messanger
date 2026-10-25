using Messanger.Infrastructure;
using Messanger.IntegrationTests.Api;
using Messanger.IntegrationTests.Repositories;
using Messanger.IntegrationTests.Maintenance;
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

(string Name, Func<Task> Run)[] workerTests =
[
    (nameof(RefreshTokenCleanupWorkerTests.FailureRetriesOnScheduleWithANewDisposedScope), RefreshTokenCleanupWorkerTests.FailureRetriesOnScheduleWithANewDisposedScope),
    (nameof(RefreshTokenCleanupWorkerTests.ShutdownCancelsDatabaseWorkAndDisposesItsScope), RefreshTokenCleanupWorkerTests.ShutdownCancelsDatabaseWorkAndDisposesItsScope),
    (nameof(RefreshTokenCleanupWorkerTests.EachPassHonorsItsBatchBudgetBeforeWaiting), RefreshTokenCleanupWorkerTests.EachPassHonorsItsBatchBudgetBeforeWaiting)
];
foreach (var (name, run) in workerTests)
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception}"); }

(string Name, Func<ApiTestHost, Task> Run)[] apiTests =
[
    (nameof(KeyRotationTests.PreviousPublicKeysValidateAccessAndRefreshDuringRotation), KeyRotationTests.PreviousPublicKeysValidateAccessAndRefreshDuringRotation),
    (nameof(OperationsTests.ReadinessDetectsPendingMigrationsAndRecovers), OperationsTests.ReadinessDetectsPendingMigrationsAndRecovers),
    (nameof(OperationsTests.DatabaseOutageDoesNotBreakLivenessOrExposeConnectionDetails), OperationsTests.DatabaseOutageDoesNotBreakLivenessOrExposeConnectionDetails),
    (nameof(OperationsTests.CorrelationAndMetricsAreSafeAndRequireACurrentAdministrator), OperationsTests.CorrelationAndMetricsAreSafeAndRequireACurrentAdministrator),
    (nameof(OperationsTests.ProductionPersistsEncryptedProtectionKeysAndRequiresHttps), OperationsTests.ProductionPersistsEncryptedProtectionKeysAndRequiresHttps),

    (nameof(SecurityControlsTests.RegistrationLimitsUseTheRealPeerAndIgnoreUntrustedForwarders), SecurityControlsTests.RegistrationLimitsUseTheRealPeerAndIgnoreUntrustedForwarders),
    (nameof(SecurityControlsTests.LoginLimitsBothIpAndNormalizedAccountWithoutEnumeratingUsers), SecurityControlsTests.LoginLimitsBothIpAndNormalizedAccountWithoutEnumeratingUsers),
    (nameof(SecurityControlsTests.SessionThrottlingDoesNotRotateOrClearTheRefreshCookie), SecurityControlsTests.SessionThrottlingDoesNotRotateOrClearTheRefreshCookie),
    (nameof(SecurityControlsTests.PasswordConfirmationBudgetIsSharedAcrossPeerAddresses), SecurityControlsTests.PasswordConfirmationBudgetIsSharedAcrossPeerAddresses),
    (nameof(SecurityControlsTests.MessageBudgetsCoverRepliesAndAllUserConnections), SecurityControlsTests.MessageBudgetsCoverRepliesAndAllUserConnections),
    (nameof(SecurityControlsTests.ExcessiveHubCallsCloseTheAbusiveConnection), SecurityControlsTests.ExcessiveHubCallsCloseTheAbusiveConnection),
    (nameof(SecurityControlsTests.ConnectionQuotasProtectExistingClientsAndReleaseSlots), SecurityControlsTests.ConnectionQuotasProtectExistingClientsAndReleaseSlots),
    (nameof(SecurityControlsTests.ReconnectingDoesNotResetConnectionAttemptBudgets), SecurityControlsTests.ReconnectingDoesNotResetConnectionAttemptBudgets),
    (nameof(SecurityControlsTests.GlobalBudgetBoundsRequestsAcrossDifferentPeers), SecurityControlsTests.GlobalBudgetBoundsRequestsAcrossDifferentPeers),
    (nameof(SecurityControlsTests.LimitsRecoverAfterTheirWindowWithoutRestarting), SecurityControlsTests.LimitsRecoverAfterTheirWindowWithoutRestarting),
    (nameof(SecurityControlsTests.CredentialConcurrencyRejectsBeforePasswordHashing), SecurityControlsTests.CredentialConcurrencyRejectsBeforePasswordHashing),
    (nameof(SecurityControlsTests.SecurityLogsAreSampledAndNeverContainCredentialsOrTokens), SecurityControlsTests.SecurityLogsAreSampledAndNeverContainCredentialsOrTokens),
    (nameof(SecurityControlsTests.InvalidSecuritySettingsFailStartup), SecurityControlsTests.InvalidSecuritySettingsFailStartup),

    (nameof(RefreshTokenRetentionTests.OnlyRecordsExpiredForAtLeastNinetyDaysAreEligible), RefreshTokenRetentionTests.OnlyRecordsExpiredForAtLeastNinetyDaysAreEligible),
    (nameof(RefreshTokenRetentionTests.NewerFamilyRecordsPreserveOlderReplayEvidence), RefreshTokenRetentionTests.NewerFamilyRecordsPreserveOlderReplayEvidence),
    (nameof(RefreshTokenRetentionTests.BatchesAreBoundedResumableAndIdempotent), RefreshTokenRetentionTests.BatchesAreBoundedResumableAndIdempotent),
    (nameof(RefreshTokenRetentionTests.ConcurrentCleanersNeverDeleteTheSameRowTwice), RefreshTokenRetentionTests.ConcurrentCleanersNeverDeleteTheSameRowTwice),
    (nameof(RefreshTokenRetentionTests.LockedRecordsAreSkippedRatherThanBlockingCleanup), RefreshTokenRetentionTests.LockedRecordsAreSkippedRatherThanBlockingCleanup),
    (nameof(RefreshTokenRetentionTests.ActiveSessionsAndConsumedTokenReplayDetectionSurviveCleanup), RefreshTokenRetentionTests.ActiveSessionsAndConsumedTokenReplayDetectionSurviveCleanup),
    (nameof(RefreshTokenRetentionTests.CancellationKeepsCommittedBatchesAndAllowsALaterResume), RefreshTokenRetentionTests.CancellationKeepsCommittedBatchesAndAllowsALaterResume),

    (nameof(PasswordChangeTests.ChangeRevokesEverySessionAndClosesLiveConnections), PasswordChangeTests.ChangeRevokesEverySessionAndClosesLiveConnections),
    (nameof(PasswordChangeTests.WrongCurrentPasswordPreservesTheExistingSession), PasswordChangeTests.WrongCurrentPasswordPreservesTheExistingSession),
    (nameof(PasswordChangeTests.PasswordContractsRequireAuthenticationHttpsAndValidInput), PasswordChangeTests.PasswordContractsRequireAuthenticationHttpsAndValidInput),
    (nameof(PasswordChangeTests.ConcurrentVerifiedChangesHaveExactlyOneWinner), PasswordChangeTests.ConcurrentVerifiedChangesHaveExactlyOneWinner),
    (nameof(PasswordChangeTests.LoginAndRefreshRacesCannotPreserveAnOldPasswordSession), PasswordChangeTests.LoginAndRefreshRacesCannotPreserveAnOldPasswordSession),
    (nameof(PasswordChangeTests.InactiveAccountsCannotChangePasswordsFromPreviouslyTrackedScopes), PasswordChangeTests.InactiveAccountsCannotChangePasswordsFromPreviouslyTrackedScopes),

    (nameof(ChatReadTests.UnreadCountsIncludeOwnMessagesAndFetchingDoesNotMarkRead), ChatReadTests.UnreadCountsIncludeOwnMessagesAndFetchingDoesNotMarkRead),
    (nameof(ChatReadTests.ReadMarkersAreMonotonicAndNoOpsKeepTheirTimestamp), ChatReadTests.ReadMarkersAreMonotonicAndNoOpsKeepTheirTimestamp),
    (nameof(ChatReadTests.ReadContractsRejectInvalidMessagesAndOutsiders), ChatReadTests.ReadContractsRejectInvalidMessagesAndOutsiders),
    (nameof(ChatReadTests.ConcurrentTabsCreateOneMarkerAtTheGreatestReadId), ChatReadTests.ConcurrentTabsCreateOneMarkerAtTheGreatestReadId),
    (nameof(ChatReadTests.LiveSummariesArePersonalizedAndReadChangesReachEveryReaderTab), ChatReadTests.LiveSummariesArePersonalizedAndReadChangesReachEveryReaderTab),
    (nameof(ChatReadTests.RejoiningRetainsReadProgressAndCountsMessagesSentWhileAway), ChatReadTests.RejoiningRetainsReadProgressAndCountsMessagesSentWhileAway),
    (nameof(ChatReadTests.RepositoryRechecksAccessAfterRemovalOrSuspension), ChatReadTests.RepositoryRechecksAccessAfterRemovalOrSuspension),

    (nameof(BrowserSessionTests.LoginIssuesPrivatePersistentCookieAndHashedRecord), BrowserSessionTests.LoginIssuesPrivatePersistentCookieAndHashedRecord),
    (nameof(BrowserSessionTests.ReopeningRestoresSessionAndRotatesWithoutExtendingExpiry), BrowserSessionTests.ReopeningRestoresSessionAndRotatesWithoutExtendingExpiry),
    (nameof(BrowserSessionTests.ReplayedTokenRevokesFamilyAndClosesLiveChat), BrowserSessionTests.ReplayedTokenRevokesFamilyAndClosesLiveChat),
    (nameof(BrowserSessionTests.ConcurrentRefreshFailsClosedWithoutTwoSuccessors), BrowserSessionTests.ConcurrentRefreshFailsClosedWithoutTwoSuccessors),
    (nameof(BrowserSessionTests.LogoutIsIdempotentAndRevokesOnlyItsFamily), BrowserSessionTests.LogoutIsIdempotentAndRevokesOnlyItsFamily),
    (nameof(BrowserSessionTests.SessionEndpointsRequireCsrfProofAndSameOriginHttps), BrowserSessionTests.SessionEndpointsRequireCsrfProofAndSameOriginHttps),
    (nameof(BrowserSessionTests.AbsoluteDeadlineCapsAccessAndNeverSlides), BrowserSessionTests.AbsoluteDeadlineCapsAccessAndNeverSlides),

    (nameof(AccountApiTests.ConcurrentDeletionsPreserveAnActiveAdministrator), AccountApiTests.ConcurrentDeletionsPreserveAnActiveAdministrator),
    (nameof(AccountApiTests.AdministratorSuspensionRevokesSessionsAtomically), AccountApiTests.AdministratorSuspensionRevokesSessionsAtomically),
    (nameof(AccountApiTests.AdministratorRoleIsReadFromDatabase), AccountApiTests.AdministratorRoleIsReadFromDatabase),
    (nameof(AccountApiTests.AccountContractsRejectInvalidTransitions), AccountApiTests.AccountContractsRejectInvalidTransitions),
    (nameof(AccountApiTests.DeletionPreservesMessagesAndRequiresOwnershipTransfer), AccountApiTests.DeletionPreservesMessagesAndRequiresOwnershipTransfer),
    (nameof(AccountApiTests.SuspensionClosesJoinedAndIdleConnections), AccountApiTests.SuspensionClosesJoinedAndIdleConnections),
    (nameof(AccountApiTests.ConcurrentStatusChangesCannotReviveOldTokens), AccountApiTests.ConcurrentStatusChangesCannotReviveOldTokens),

    (nameof(ApiContractTests.DevelopmentPublishesAuthenticatedContracts), ApiContractTests.DevelopmentPublishesAuthenticatedContracts),
    (nameof(ApiContractTests.ProductionDoesNotPublishContracts), ApiContractTests.ProductionDoesNotPublishContracts),
    (nameof(ChatMembershipRealtimeTests.RemovalRevokesEveryJoinedConnectionImmediately), ChatMembershipRealtimeTests.RemovalRevokesEveryJoinedConnectionImmediately),
    (nameof(ChatMembershipRealtimeTests.RejoinedMemberMustJoinAgainBeforeDelivery), ChatMembershipRealtimeTests.RejoinedMemberMustJoinAgainBeforeDelivery),
    (nameof(ChatMembershipRealtimeTests.InvalidHubArgumentsUseStableBusinessErrors), ChatMembershipRealtimeTests.InvalidHubArgumentsUseStableBusinessErrors),
    (nameof(ChatMembershipApiTests.ReusedScopeCannotRetainFormerOwnerPermissions), ChatMembershipApiTests.ReusedScopeCannotRetainFormerOwnerPermissions),
    (nameof(ChatMembershipApiTests.OwnerAddsAndRejoinsWithoutDuplicatingMembership), ChatMembershipApiTests.OwnerAddsAndRejoinsWithoutDuplicatingMembership),
    (nameof(ChatMembershipApiTests.ModeratorsCanRemoveOnlyOrdinaryMembers), ChatMembershipApiTests.ModeratorsCanRemoveOnlyOrdinaryMembers),
    (nameof(ChatMembershipApiTests.OwnerTransfersBeforeLeaving), ChatMembershipApiTests.OwnerTransfersBeforeLeaving),
    (nameof(ChatMembershipApiTests.ConcurrentTransfersKeepExactlyOneOwner), ChatMembershipApiTests.ConcurrentTransfersKeepExactlyOneOwner),
    (nameof(ChatMembershipApiTests.MembershipContractsRejectInvalidInput), ChatMembershipApiTests.MembershipContractsRejectInvalidInput),
    (nameof(ChatMembershipApiTests.LeaveIsIdempotentAndInactiveTargetsAreRejected), ChatMembershipApiTests.LeaveIsIdempotentAndInactiveTargetsAreRejected),
    (nameof(ChatMembershipApiTests.ConcurrentAddsRespectTheMemberLimit), ChatMembershipApiTests.ConcurrentAddsRespectTheMemberLimit),
    (nameof(ChatRealtimeTests.BrowserWebSocketAuthenticatesWithHubQueryToken), ChatRealtimeTests.BrowserWebSocketAuthenticatesWithHubQueryToken),
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

var total = tests.Length + workerTests.Length + apiTests.Length;
Console.WriteLine(
    $"{total - failures}/{total} integration checks passed. Repository transactions rolled back; API test schema removed.");
return failures == 0 ? 0 : 1;