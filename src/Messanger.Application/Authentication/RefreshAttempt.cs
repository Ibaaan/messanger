namespace Messanger.Application.Authentication;

public sealed record RefreshAttempt(SessionResult? Session, SessionIdentity? RevokedFamily);
