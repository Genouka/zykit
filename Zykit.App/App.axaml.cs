using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Enums;
using SukiUI.Toasts;
using Zykit.App.Services;
using Zykit.App.ViewModels;
using Zykit.App.Views;

namespace Zykit.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string[]? StartupArgs { get; private set; }

    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 捕获启动参数（可能包含 hokit:// 或 zykit:// 协议 URL）
            StartupArgs = desktop.Args;

            _mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.MainWindow = _mainWindow;

            // 订阅单实例管道消息：当已运行实例收到协议唤起时，在 UI 线程处理
            if (Program.SingleInstance != null)
            {
                Program.SingleInstance.MessageReceived += OnProtocolMessageReceived;
            }

            // 处理首次启动时的协议参数。
            // 此时窗口尚未显示，SukiSideMenu 未完成布局，直接导航会失败。
            // 延迟到窗口 Opened 后再处理，确保控件已就绪。
            var firstLaunchArgs = StartupArgs;
            if (HasProtocolArg(firstLaunchArgs))
            {
                _mainWindow.Opened += (s, e) => HandleProtocolInvocation(firstLaunchArgs);
            }
            else
            {
                // 无协议参数时，首次启动检测 Hokit 环境
                _mainWindow.Opened += (s, e) => OnFirstLaunchDetectHokit();
                // 首次启动自动注册 zykit:// 协议
                _mainWindow.Opened += (s, e) => OnFirstLaunchAutoRegisterZykitProtocol();
            }

            // 自动检查更新（无协议参数时）
            if (!HasProtocolArg(firstLaunchArgs))
            {
                try
                {
                    var appSettings = Services.GetRequiredService<AppSettingsService>();
                    if (appSettings.AutoCheckUpdate)
                    {
                        _ = AutoCheckUpdateAsync(Services);
                    }
                }
                catch { /* 检测失败不应影响启动 */ }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>判断参数中是否包含真正的协议调用（hokit:// 或 zykit://，带查询参数）</summary>
    private static bool HasProtocolArg(string[]? args)
    {
        if (args == null) return false;
        return args.Any(a => IsRealProtocolInvocation(a));
    }

    /// <summary>
    /// 判断单个参数是否是真正的协议调用（hokit:// 或 zykit://）。
    /// 网页的 protocolCheck("hokit://") 探针不带查询参数，不是真正的调用，应忽略。
    /// </summary>
    private static bool IsRealProtocolInvocation(string arg)
    {
        if (!LooksLikeProtocolUrl(arg)) return false;
        var decoded = arg;
        if (!StartsWithKnownScheme(decoded))
        {
            try { decoded = Uri.UnescapeDataString(decoded); } catch { }
        }
        return decoded.Contains('?');
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

    /// <summary>
    /// 判断单个参数是否是 hokit:// 或 zykit:// 协议 URL。
    /// 兼容浏览器对整个 URL 进行百分号编码的情况（如 hokit%3A%2F%2F...）。
    /// </summary>
    private static bool LooksLikeProtocolUrl(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return false;
        if (StartsWithKnownScheme(arg))
            return true;
        // 浏览器可能对整个 URL 百分号编码：hokit%3A%2F%2F...
        try
        {
            var decoded = Uri.UnescapeDataString(arg);
            return StartsWithKnownScheme(decoded);
        }
        catch { return false; }
    }

    /// <summary>协议诊断日志（写入临时目录，便于排查协议唤起问题）</summary>
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

    /// <summary>首次启动（无协议参数）时检测 Hokit 环境</summary>
    private void OnFirstLaunchDetectHokit()
    {
        try
        {
            var appSettings = Services.GetRequiredService<AppSettingsService>();
            if (appSettings.DetectHokitOnFirstLaunch())
            {
                var toast = Services.GetRequiredService<ISukiToastManager>();
                toast.CreateToast()
                    .WithTitle("已启用 Hokit 兼容模式")
                    .WithContent("检测到本机已安装 HoKit，已自动启用 Hokit 兼容模式（可在「设置」中关闭）")
                    .OfType(NotificationType.Information)
                    .Dismiss().After(TimeSpan.FromSeconds(6))
                    .Dismiss().ByClicking()
                    .Queue();
            }
        }
        catch { /* 检测失败不应影响启动 */ }
    }

    /// <summary>首次启动（无协议参数）时自动注册 zykit:// 协议</summary>
    private void OnFirstLaunchAutoRegisterZykitProtocol()
    {
        try
        {
            var appSettings = Services.GetRequiredService<AppSettingsService>();
            var protocol = Services.GetRequiredService<HokitProtocolService>();
            if (appSettings.AutoRegisterZykitProtocolOnFirstLaunch(protocol))
            {
                var toast = Services.GetRequiredService<ISukiToastManager>();
                toast.CreateToast()
                    .WithTitle("已注册 zykit:// 协议")
                    .WithContent("首次启动已自动注册 zykit:// 协议，浏览器点击链接可唤起本程序（可在「设置」中取消注册）")
                    .OfType(NotificationType.Information)
                    .Dismiss().After(TimeSpan.FromSeconds(6))
                    .Dismiss().ByClicking()
                    .Queue();
            }
        }
        catch { /* 注册失败不应影响启动 */ }
    }

    /// <summary>
    /// 单实例管道消息回调（在后台线程触发）：收到其他实例发来的协议 URL 后，
    /// 切到 UI 线程处理协议唤起。
    /// </summary>
    private void OnProtocolMessageReceived(string message)
    {
        Dispatcher.UIThread.Post(() => HandleProtocolInvocation(new[] { message }));
    }

    /// <summary>
    /// 处理 hokit:// 或 zykit:// 协议唤起：解析协议 URL，存储待处理数据，刷新联动操作页并跳转。
    /// 必须在 UI 线程调用。
    /// </summary>
    private void HandleProtocolInvocation(string[]? args)
    {
        if (args == null || args.Length == 0 || _mainWindow == null) return;

        // 诊断日志：记录所有原始参数
        LogProtocol($"HandleProtocolInvocation 收到 {args.Length} 个参数:");
        for (int i = 0; i < args.Length; i++)
            LogProtocol($"  Arg[{i}] = {args[i]}");

        try
        {
            var hokitProtocol = Services.GetRequiredService<HokitProtocolService>();
            var toast = Services.GetRequiredService<ISukiToastManager>();
            var linkageVm = Services.GetRequiredService<LinkageViewModel>();

            // 查找 hokit:// 或 zykit:// URL。
            // 某些情况下 Windows shell 可能将 URL 中的 & 解释为命令分隔符，
            // 导致 URL 被分割成多个 args，这里把以已知 scheme 开头的参数及其后
            // 看起来像 URL 参数片段（含 = 且不以 - 开头）的参数合并。
            // 同时兼容浏览器对整个 URL 百分号编码的情况。
            string? protocolUrl = null;
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                // 先尝试原始形式，再尝试百分号解码
                if (!LooksLikeProtocolUrl(arg)) continue;

                // 如果是编码形式，先解码
                if (!StartsWithKnownScheme(arg))
                {
                    try { arg = Uri.UnescapeDataString(arg); } catch { }
                }

                protocolUrl = arg;
                for (int j = i + 1; j < args.Length; j++)
                {
                    var next = args[j];
                    if (next.StartsWith("-")) break;
                    if (next.Contains("=")) protocolUrl += "&" + next;
                    else break;
                }
                break;
            }

            if (string.IsNullOrEmpty(protocolUrl))
            {
                LogProtocol("未找到 hokit:// 或 zykit:// URL，退出处理");
                return;
            }

            LogProtocol($"待解析 URL: {protocolUrl}");

            // 网页常通过 protocolCheck("hokit://") 或 protocolCheck("zykit://") 检测协议是否注册，
            // 此时会以无查询参数的 URL（如 hokit:///）唤起本程序。
            // 这不是真正的下载请求，静默忽略，避免弹出"协议解析失败" Toast。
            if (!protocolUrl.Contains('?'))
            {
                LogProtocol($"URL 无查询参数，疑为协议检测探针，静默忽略: {protocolUrl}");
                return;
            }

            var data = hokitProtocol.ParseUrl(protocolUrl);
            if (data == null)
            {
                // 显示收到的 URL 片段用于诊断
                var preview = protocolUrl.Length > 80 ? protocolUrl.Substring(0, 80) + "..." : protocolUrl;
                LogProtocol($"解析失败，URL 预览: {preview}");
                toast.CreateToast()
                    .WithTitle("协议解析失败")
                    .WithContent($"收到: {preview}\n完整参数已记录到日志: %TEMP%\\Zykit\\protocol.log")
                    .OfType(NotificationType.Error)
                    .Dismiss().After(TimeSpan.FromSeconds(8))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            LogProtocol($"解析成功: Url={data.Url}, Headers={data.Headers.Count}, Source={data.Source}");

            hokitProtocol.PendingData = data;

            // 刷新联动操作页的数据并重置向导状态（LinkageViewModel 是单例，
            // 无论是否已访问过该页面，都能正确刷新）
            linkageVm.LoadPendingData();

            // 跳转到联动操作页面（动态添加到侧边栏，默认不显示）
            _mainWindow.ShowLinkageMenuItem();

            // 激活窗口（协议唤起时窗口可能在后台）
            try
            {
                _mainWindow.Activate();
                _mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            }
            catch { /* 激活失败不影响导航 */ }

            toast.CreateToast()
                .WithTitle("联动操作")
                .WithContent("已接收下载请求，请确认下载并完成安装")
                .OfType(NotificationType.Information)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            LogProtocol($"HandleProtocolInvocation 异常: {ex}");
            /* 协议处理失败不应影响启动 */
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // SukiUI Toast 管理器 (单例，供 MainWindow 与各 ViewModel 共享)
        services.AddSingleton<ISukiToastManager, SukiToastManager>();

        // Services
        services.AddSingleton<ISdkPathProvider, SdkPathProvider>();
        services.AddSingleton<HdcService>();
        services.AddSingleton<SignService>();
        services.AddSingleton<LogService>();
        services.AddSingleton<EcoService>();
        services.AddSingleton<KeyStoreService>();
        services.AddSingleton<LocalCacheService>();
        services.AddSingleton<PairingService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<BuildService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<HokitProtocolService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<AccountViewModel>();
        services.AddTransient<DeviceViewModel>();
        services.AddTransient<SigningViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<PairingViewModel>();
        services.AddTransient<InstallViewModel>();
        services.AddTransient<RealDeviceDebugViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AppIdViewModel>();
        services.AddTransient<WizardViewModel>();
        services.AddSingleton<LinkageViewModel>();
    }

    /// <summary>
    /// 启动时自动检查更新（静默）：仅在有新版本时弹出带下载按钮的 Toast。
    /// </summary>
    private static async Task AutoCheckUpdateAsync(IServiceProvider sp)
    {
        try
        {
            var update = sp.GetRequiredService<UpdateService>();
            var toast = sp.GetRequiredService<ISukiToastManager>();

            var info = await update.FetchLatestAsync();
            if (!UpdateService.IsNewer(update.CurrentVersion, info.LatestVersion)) return;

            var url = string.IsNullOrWhiteSpace(info.DownloadUrl)
                ? UpdateService.HomePageUrl
                : info.DownloadUrl;

            var content = $"新版本 v{info.LatestVersion} 已发布，当前版本 v{update.CurrentVersion}";
            if (!string.IsNullOrWhiteSpace(info.ReleaseDate))
                content += $"\n发布日期: {info.ReleaseDate}";
            if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
                content += $"\n更新说明: {info.ReleaseNotes}";

            toast.CreateToast()
                .WithTitle("发现新版本")
                .WithContent(content)
                .OfType(NotificationType.Information)
                .WithActionButton("立即更新", _ =>
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch { /* ignore */ }
                }, true)
                .WithActionButton("稍后", _ => { }, true, SukiButtonStyles.Accent)
                .Queue();
        }
        catch { /* 自动检查失败不应影响用户 */ }
    }
}
