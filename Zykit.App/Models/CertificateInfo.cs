namespace Zykit.App.Models;

public class CertificateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int CertType { get; set; } // 1=调试, 2=发布
    public string CertTypeName => CertType == 1 ? "调试证书" : "发布证书";
    public string CertObjectId { get; set; } = "";
    public string LocalPath { get; set; } = "";
}
