namespace Zykit.App.Models;

public class HapPackage
{
    public string FilePath { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string PackageName { get; set; } = "";
    public string AppName { get; set; } = "";
    public long FileSize { get; set; }
    public string ModuleJson { get; set; } = "";
}
