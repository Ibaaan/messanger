namespace Messanger.Application.Authentication;

public sealed record SignedRefreshToken(string Value, RefreshCredential Credential);
