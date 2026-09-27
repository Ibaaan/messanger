using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.Infrastructure.Authentication;

public sealed class JwtSigningKey : IDisposable
{
    private readonly RSA rsa;

    public JwtSigningKey(IOptions<JwtOptions> options)
    {
        rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(options.Value.PrivateKeyPath));
            if (rsa.KeySize < 2048)
                throw new InvalidOperationException("JWT signing key must be at least 2048 bits.");
            // A public-key-only PEM is insufficient for issuing tokens.
            rsa.ExportParameters(true);
            Key = new RsaSecurityKey(rsa)
            {
                KeyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16]
            };
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public RsaSecurityKey Key { get; }

    public void Dispose()
    {
        rsa.Dispose();
    }
}