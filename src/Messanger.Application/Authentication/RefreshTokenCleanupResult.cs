namespace Messanger.Application.Authentication;

public sealed record RefreshTokenCleanupResult(DateTimeOffset Cutoff, int DeletedRows, bool BatchLimitReached);
