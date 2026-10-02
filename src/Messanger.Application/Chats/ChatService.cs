using System.Text.RegularExpressions;
using Messanger.Application.Messages;
using Messanger.Application.Persistence;
using Messanger.Domain;

namespace Messanger.Application.Chats;

public sealed partial class ChatService(IUserRepository users, IChatRepository chats, IMessageRepository messages, ChatAccessService access)
{
    public async Task<IReadOnlyList<UserSummary>> SearchUsersAsync(long callerId, string? query, CancellationToken cancellationToken)
    {
        await access.RequireActiveAsync(callerId, cancellationToken);
        query = query?.Trim().TrimStart('@');
        if (query is null || !SearchPattern().IsMatch(query))
            throw new UseCaseException("validation_failed", "Search must contain 1–20 ASCII letters, digits, or underscores.");
        return await users.SearchAsync(query, callerId, cancellationToken);
    }

    public async Task<ChatResponse> CreateAsync(long callerId, string? name, string? description,
        long[]? userIds, CancellationToken cancellationToken)
    {
        await access.RequireActiveAsync(callerId, cancellationToken);
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || description?.Length > 2000)
            throw new UseCaseException("validation_failed", "A chat needs a name of 1–120 characters and a description of at most 2000 characters.");
        if (userIds is null || userIds.Length > 50 || userIds.Any(id => id <= 0))
            throw new UseCaseException("validation_failed", "Select 1–50 participants.");
        var participants = userIds.Where(id => id != callerId).Distinct().ToArray();
        if (participants.Length == 0)
            throw new UseCaseException("validation_failed", "Select at least one other participant.");
        foreach (var id in participants)
            if ((await users.GetByIdAsync(id, cancellationToken))?.AccountStatus != "ACTIVE")
                throw new UseCaseException("validation_failed", "Every participant must be an existing active user.");
        var chat = new Chat { Name = name, Description = description };
        await chats.CreateAsync(chat, callerId, participants, cancellationToken);
        return await DescribeAsync(chat, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatResponse>> ListAsync(long callerId, CancellationToken cancellationToken)
    {
        await access.RequireActiveAsync(callerId, cancellationToken);
        var result = new List<ChatResponse>();
        foreach (var chat in await chats.GetForUserAsync(callerId, cancellationToken))
            result.Add(await DescribeAsync(chat, cancellationToken));
        return result;
    }

    public async Task<ChatDetailsResponse> GetAsync(long chatId, long callerId, CancellationToken cancellationToken)
    {
        await access.RequireMemberAsync(chatId, callerId, cancellationToken);
        var chat = await chats.GetByIdAsync(chatId, cancellationToken)
            ?? throw new UseCaseException("not_found", "Chat not found.");
        var history = await messages.GetHistoryResponsesAsync(chatId, null, 50, cancellationToken);
        return new ChatDetailsResponse(await DescribeAsync(chat, cancellationToken), history.Reverse().ToArray());
    }

    private async Task<ChatResponse> DescribeAsync(Chat chat, CancellationToken cancellationToken)
    {
        var members = await chats.GetMembersAsync(chat.Id, cancellationToken);
        var latest = await messages.GetHistoryResponsesAsync(chat.Id, null, 1, cancellationToken);
        return new ChatResponse(chat.Id, chat.Name ?? "Group", chat.Description, chat.CreatedAt, members, latest.FirstOrDefault());
    }

    [GeneratedRegex("\\A[A-Za-z0-9_]{1,20}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SearchPattern();
}
