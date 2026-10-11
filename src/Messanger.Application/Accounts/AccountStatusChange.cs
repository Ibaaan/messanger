namespace Messanger.Application.Accounts;

public sealed record AccountStatusChange(AccountResponse Account, int SessionVersion);
