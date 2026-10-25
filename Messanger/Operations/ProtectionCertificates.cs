using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace Messanger.Operations;

public sealed class ProtectionCertificates : ICertificateResolver, IDisposable
{
    private readonly Lazy<X509Certificate2[]> certificates;
    public ProtectionCertificates(IOptions<ProtectionStorageOptions> settings)
    {
        certificates = new(() => string.IsNullOrEmpty(settings.Value.CertificatePath) ? [] :
            new[] { settings.Value.CertificatePath }.Concat(settings.Value.PreviousCertificatePaths)
                .Select(path => X509CertificateLoader.LoadPkcs12FromFile(path, settings.Value.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet)).ToArray());
    }
    public X509Certificate2[] All => certificates.Value;
    public X509Certificate2? Active => certificates.Value.FirstOrDefault();
    public X509Certificate2? ResolveCertificate(string thumbprint)
    {
        var certificate = certificates.Value.FirstOrDefault(value => value.Thumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase));
        return certificate is null ? null : new X509Certificate2(certificate);
    }
    public void Dispose() { if (certificates.IsValueCreated) foreach (var certificate in certificates.Value) certificate.Dispose(); }
}
