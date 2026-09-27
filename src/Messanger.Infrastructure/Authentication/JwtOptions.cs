namespace Messanger.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
}