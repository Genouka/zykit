namespace Zykit.App.Services;

public interface ISdkPathProvider
{
    string SdkPath { get; set; }
    string HdcPath { get; }
    string SignToolJarPath { get; }
    string JavaPath { get; }
    string KeytoolPath { get; }
    string OpenHarmonyP12Path { get; }
    string DebugProfileTemplatePath { get; }
    string PermissionDefinitionsPath { get; }
    string ConfigDir { get; }

    /// <summary>内置 Hokit 兼容模式密钥库路径 (tools/3rd/ho-kit.p12)</summary>
    string HokitKeystorePath { get; }

    /// <summary>内置 Hokit 兼容模式 CSR 路径 (tools/3rd/ho-kit.csr)</summary>
    string HokitCsrPath { get; }
}
