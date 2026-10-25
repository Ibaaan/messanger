using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace Messanger.Operations;

internal static class ProtectionStorage
{
    public static IServiceCollection AddProtectionStorage(this IServiceCollection services, IConfiguration configuration, bool development)
    {
        services.AddOptions<ProtectionStorageOptions>().Bind(configuration.GetSection("DataProtection"))
            .Validate(options => development || (!string.IsNullOrWhiteSpace(options.KeysPath) && !string.IsNullOrWhiteSpace(options.CertificatePath)),
                "Production requires DataProtection:KeysPath and CertificatePath.")
            .Validate(options => string.IsNullOrEmpty(options.CertificatePath) || File.Exists(options.CertificatePath), "Data Protection certificate is missing.")
            .Validate(options => options.PreviousCertificatePaths.All(File.Exists), "A previous Data Protection certificate is missing.")
            .ValidateOnStart();
        services.AddSingleton<ICertificateResolver>(provider => provider.GetRequiredService<ProtectionCertificates>());
        var protection = services.AddDataProtection().SetApplicationName("Messanger");
        var settings = configuration.GetSection("DataProtection").Get<ProtectionStorageOptions>() ?? new();
        if (!string.IsNullOrEmpty(settings.CertificatePath))
        {
            var certificates = new ProtectionCertificates(Options.Create(settings));
            services.AddSingleton<ProtectionCertificates>(_ => certificates);
            protection.UnprotectKeysWithAnyCertificate(certificates.All);
        }
        else services.AddSingleton<ProtectionCertificates>();
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<ProtectionStorageOptions>, ProtectionCertificates, ILoggerFactory>((options, storage, certificates, logs) =>
        {
            if (!string.IsNullOrEmpty(storage.Value.KeysPath)) options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(storage.Value.KeysPath), logs);
            if (certificates.Active is { } certificate) options.XmlEncryptor = new CertificateXmlEncryptor(certificate, logs);
        });
        return services;
    }
}
