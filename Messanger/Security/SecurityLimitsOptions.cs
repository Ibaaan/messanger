namespace Messanger.Security;

public sealed class SecurityLimitsOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    public int RequestsPerServer { get; set; } = 10000;
    public int RequestsPerIp { get; set; } = 600;
    public int RegistrationPerIp { get; set; } = 5;
    public int LoginPerIp { get; set; } = 30;
    public int LoginPerAccount { get; set; } = 10;
    public int SessionsPerIp { get; set; } = 120;
    public int PasswordChangesPerUser { get; set; } = 5;
    public int CredentialConcurrency { get; set; } = 4;
    public int HubCallsPerUser { get; set; } = 120;
    public int MessagesPerUser { get; set; } = 30;
    public int ConnectionAttemptsPerUser { get; set; } = 30;
    public int MaximumConnectionsPerUser { get; set; } = 5;
    public int MaximumConnections { get; set; } = 1000;
}
