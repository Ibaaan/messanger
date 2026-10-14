namespace Messanger.Application.Authentication;

public sealed record RefreshCredential(long UserId, int SessionVersion, Guid FamilyId, string JtiHash, DateTimeOffset ExpiresAt);
