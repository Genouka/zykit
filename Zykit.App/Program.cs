using Avalonia;
using System;
using System.IO;
using System.Linq;
using Zykit.App.Services;

namespace Zykit.App;

sealed class Program
{
    // 单实例服务，在 Main 中创建，供 App.axaml.cs 订阅消息
    public static SingleInstanceService? SingleInstance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var singleInstance = new SingleInstanceService();

        if (!singleInstance.TryAcquire())
        {
            // 已有实例运行：将协议 URL 转发给第一个实例，然后退出
            // 支持 hokit:// 和 zykit:// 两种 scheme，兼容百分号编码
            var protocolArg = args.FirstOrDefault(a => LooksLikeProtocolUrl(a));
            if (protocolArg != null)
            {
                // 如果是编码形式，先解码再转发
                if (!StartsWithKnownScheme(protocolArg))
                {
                    try { protocolArg = Uri.UnescapeDataString(protocolArg); } catch { }
                }
                LogProtocol($"第二个实例转发 URL: {protocolArg}");
                singleInstance.SendToFirstInstance(protocolArg);
            }
            return;
        }

        // 第一个实例：保存引用并启动管道服务器监听后续协议唤起
        SingleInstance = singleInstance;
        SingleInstance.StartServer();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Stop();
        }
    }

    /// <summary>支持的协议 scheme（小写）</summary>
    private static readonly string[] SupportedSchemes = { "hokit://", "zykit://" };

    /// <summary>判断字符串是否以已知 scheme 开头（不区分大小写）</summary>
    private static bool StartsWithKnownScheme(string arg)
    {
        foreach (var scheme in SupportedSchemes)
        {
            if (arg.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>判断参数是否是 hokit:// 或 zykit:// 协议 URL（兼容百分号编码）</summary>
    private static bool LooksLikeProtocolUrl(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return false;
        if (StartsWithKnownScheme(arg))
            return true;
        try
        {
            var decoded = Uri.UnescapeDataString(arg);
            return StartsWithKnownScheme(decoded);
        }
        catch { return false; }
    }

    /// <summary>协议诊断日志</summary>
    private static void LogProtocol(string message)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "Zykit", "protocol.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
