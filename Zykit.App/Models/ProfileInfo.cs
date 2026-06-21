namespace Zykit.App.Models;

public class ProfileInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string Type { get; set; } = "debug"; // debug or release
}
