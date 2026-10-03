using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}
