namespace Messanger.Operations;

public sealed class ProtectionStorageOptions
{
    public string KeysPath { get; set; } = "";
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
    public string[] PreviousCertificatePaths { get; set; } = [];
}
