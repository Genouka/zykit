namespace Zykit.App.Models;

public class SigningConfig
{
    public string KeystorePath { get; set; } = "";
    public string KeystorePassword { get; set; } = "zykit123";
    public string KeyAlias { get; set; } = "zykit";
    public string KeyPassword { get; set; } = "zykit123";
    public string CertPath { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public string CertId { get; set; } = "";
}
