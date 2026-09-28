namespace Messanger.Http;

public sealed class HttpSecurityOptions
{
    public string[] AllowedOrigins { get; set; } = [];
    public string[] TrustedProxies { get; set; } = [];
}