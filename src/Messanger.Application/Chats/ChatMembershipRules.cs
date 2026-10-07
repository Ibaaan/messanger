using Messanger.Domain;

namespace Messanger.Application.Chats;

public static class ChatMembershipRules
{
    public const int MaximumMembers = 51; // Creator plus at most fifty other users.

    public static void RequireOwner(ChatParticipant actor)
    {
        if (actor.Role != "OWNER") throw new UseCaseException("forbidden", "Only the chat owner can perform this operation.");
    }

    public static void RequireCanRemove(ChatParticipant actor, ChatParticipant target)
    {
        if (target.Role == "OWNER")
            throw new UseCaseException("owner_transfer_required", "The owner must transfer ownership before leaving.");
        if (actor.Role != "OWNER" && !(actor.Role == "MODERATOR" && target.Role is "MEMBER" or "USER"))
            throw new UseCaseException("forbidden", "You cannot remove this participant.");
    }

    public static void RequirePositiveId(long id)
    {
        if (id <= 0) throw new UseCaseException("validation_failed", "IDs must be positive integers.");
    }

    public static string ValidateRole(string? role)
    {
        if (role is not ("MEMBER" or "MODERATOR"))
            throw new UseCaseException("validation_failed", "Role must be MEMBER or MODERATOR; use the ownership endpoint to change the owner.");
        return role;
    }

    public static string ValidateName(string? name, string? description)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || description?.Length > 2000)
            throw new UseCaseException("validation_failed", "A chat needs a name of 1–120 characters and a description of at most 2000 characters.");
        return name;
    }
}
