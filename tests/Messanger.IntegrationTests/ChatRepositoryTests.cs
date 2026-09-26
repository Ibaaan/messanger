namespace Messanger.IntegrationTests;

internal static class ChatRepositoryTests
{
    public static async Task ActiveMemberCanFindChat(TestDatabase database)
    {
        var user = await database.CreateUserAsync();
        var chat = await database.CreateChatAsync();
        await database.AddMemberAsync(chat, user);

        var isActive = await database.Chats.IsActiveParticipantAsync(chat.Id, user.Id);
        var chats = await database.Chats.GetForUserAsync(user.Id);

        TestAssert.True(isActive, "A joined member should be active.");
        TestAssert.Equal(1, chats.Count, "The member should see their chat.");
        TestAssert.Equal(chat.Id, chats[0].Id, "The returned chat should be the joined chat.");
    }

    public static async Task DepartedMemberCannotFindChat(TestDatabase database)
    {
        var user = await database.CreateUserAsync();
        var chat = await database.CreateChatAsync();
        var member = await database.AddMemberAsync(chat, user);
        member.LeftAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync();

        var isActive = await database.Chats.IsActiveParticipantAsync(chat.Id, user.Id);
        var chats = await database.Chats.GetForUserAsync(user.Id);

        TestAssert.True(!isActive, "A departed member should no longer be active.");
        TestAssert.Equal(0, chats.Count, "Departed chats should be excluded from the user's chat list.");
    }
}
