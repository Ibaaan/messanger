namespace Messanger.IntegrationTests;

internal static class MessageRepositoryTests
{
    public static async Task HistoryReturnsNewestMessagesFirst(TestDatabase database)
    {
        var user = await database.CreateUserAsync();
        var chat = await database.CreateChatAsync();
        await database.AddMemberAsync(chat, user);
        var first = await database.CreateMessageAsync(chat, user, "First");
        var second = await database.CreateMessageAsync(chat, user, "Second");

        var history = await database.Messages.GetHistoryAsync(chat.Id);
        var limitedHistory = await database.Messages.GetHistoryAsync(chat.Id, limit: 1);

        TestAssert.Equal(2, history.Count, "History should include both messages.");
        TestAssert.Equal(second.Id, history[0].Id, "The newest message should be first.");
        TestAssert.Equal(first.Id, history[1].Id, "The older message should be second.");
        TestAssert.Equal(1, limitedHistory.Count, "History should respect its limit.");
        TestAssert.Equal(second.Id, limitedHistory[0].Id, "Limited history should contain the newest message.");
    }

    public static async Task HistoryCursorExcludesNewerMessages(TestDatabase database)
    {
        var user = await database.CreateUserAsync();
        var chat = await database.CreateChatAsync();
        await database.AddMemberAsync(chat, user);
        var first = await database.CreateMessageAsync(chat, user, "First");
        var second = await database.CreateMessageAsync(chat, user, "Second");

        var history = await database.Messages.GetHistoryAsync(chat.Id, beforeId: second.Id);

        TestAssert.Equal(1, history.Count, "Only messages older than the cursor should be returned.");
        TestAssert.Equal(first.Id, history[0].Id, "The cursor message itself should be excluded.");
    }
}
