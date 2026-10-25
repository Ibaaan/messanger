using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.Infrastructure.Authentication;

public sealed class JwtSigningKey : IDisposable
{
    private readonly RSA rsa;
    private readonly List<RSA> previous = [];

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
            var validation = new List<SecurityKey> { Key };
            foreach (var path in options.Value.PreviousPublicKeyPaths)
            {
                var old = RSA.Create();
                previous.Add(old);
                old.ImportFromPem(File.ReadAllText(path));
                if (old.KeySize < 2048) throw new InvalidOperationException("Previous JWT validation keys must be at least 2048 bits.");
                validation.Add(new RsaSecurityKey(old) { KeyId = Convert.ToHexString(SHA256.HashData(old.ExportSubjectPublicKeyInfo()))[..16] });
            }
            ValidationKeys = validation;
        }
        catch
        {
            rsa.Dispose();
            foreach (var old in previous) old.Dispose();
            throw;
        }
    }

    public RsaSecurityKey Key { get; }
    public IReadOnlyList<SecurityKey> ValidationKeys { get; }

    public void Dispose()
    {
        rsa.Dispose();
        foreach (var old in previous) old.Dispose();
    }
}