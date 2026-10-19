namespace Messanger.Accounts;

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
