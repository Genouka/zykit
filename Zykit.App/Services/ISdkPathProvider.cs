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
}
