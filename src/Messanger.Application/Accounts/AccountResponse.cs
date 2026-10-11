namespace Messanger.Application.Accounts;

public sealed record AccountResponse(long Id, string Username, string Role, string AccountStatus);
