using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Toasts;
using Zykit.App.Services;
using Zykit.App.ViewModels;
using Zykit.App.Views;

namespace Zykit.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

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
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
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
    }
}
