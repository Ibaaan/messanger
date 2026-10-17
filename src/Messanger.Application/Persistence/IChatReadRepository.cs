using Messanger.Application.Chats;

namespace Messanger.Application.Persistence;

public interface IChatReadRepository
{
    Task<ChatReadState> GetAsync(long chatId, long userId, CancellationToken ct);
    // Owns the transaction; serializes updates against membership changes and competing read markers.
    Task<ChatReadState> AdvanceAsync(long chatId, long userId, long messageId, CancellationToken ct);
}
