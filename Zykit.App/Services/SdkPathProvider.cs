using System.IO;
using System;

namespace Zykit.App.Services;

public class SdkPathProvider : ISdkPathProvider
{
    private string _sdkPath = @"C:\Program Files\Huawei\DevEco Studio\sdk\default";
    private readonly string _configDir;
    private readonly string _appDir;

    public SdkPathProvider()
    {
        _appDir = AppDomain.CurrentDomain.BaseDirectory;
        _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zykit");
        Directory.CreateDirectory(_configDir);
    }

    public string SdkPath
    {
        get => _sdkPath;
        set => _sdkPath = value;
    }

    public string HdcPath => FindTool("hdc.exe", "openharmony", "toolchains", "hdc.exe");
    public string SignToolJarPath => FindTool("lib\\hap-sign-tool.jar", "openharmony", "toolchains", "lib", "hap-sign-tool.jar");
    public string JavaPath => FindJava();
    public string KeytoolPath => FindKeytool();
    public string OpenHarmonyP12Path => FindTool("lib\\OpenHarmony.p12", "openharmony", "toolchains", "lib", "OpenHarmony.p12");
    public string DebugProfileTemplatePath => FindTool("lib\\UnsgnedDebugProfileTemplate.json", "openharmony", "toolchains", "lib", "UnsgnedDebugProfileTemplate.json");
    public string PermissionDefinitionsPath => FindTool("lib\\PermissionDefinitions.json", "openharmony", "toolchains", "lib", "PermissionDefinitions.json");
    public string ConfigDir => _configDir;
    public string HokitKeystorePath => Path.Combine(_appDir, "tools", "3rd", "ho-kit.p12");
    public string HokitCsrPath => Path.Combine(_appDir, "tools", "3rd", "ho-kit.csr");

    /// <summary>
    /// 优先查找内嵌工具，找不到再从SDK路径查找
    /// </summary>
    private string FindTool(string bundledRelativePath, params string[] sdkRelativeParts)
    {
        // 优先使用内嵌工具
        var bundledPath = Path.Combine(_appDir, "tools", bundledRelativePath);
        if (File.Exists(bundledPath)) return bundledPath;

        // 回退到SDK路径
        return Path.Combine(SdkPath, Path.Combine(sdkRelativeParts));
    }

    private string FindJava()
    {
        // 优先使用内嵌JRE
        var embeddedJre = Path.Combine(_appDir, "tools", "jre", "bin", "java.exe");
        if (File.Exists(embeddedJre)) return embeddedJre;

        // 检查JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var javaPath = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaPath)) return javaPath;
        }

        return "java";
    }

    private string FindKeytool()
    {
        // 优先使用内嵌JRE
        var embeddedJre = Path.Combine(_appDir, "tools", "jre", "bin", "keytool.exe");
        if (File.Exists(embeddedJre)) return embeddedJre;

        // 检查JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var keytoolPath = Path.Combine(javaHome, "bin", "keytool.exe");
            if (File.Exists(keytoolPath)) return keytoolPath;
        }

        return "keytool";
    }
}
