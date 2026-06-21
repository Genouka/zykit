using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class AppIdViewModel : ViewModelBase
{
    private readonly EcoService _ecoService;
    private readonly AuthService _authService;

    [ObservableProperty] private string _queryPackageName = "";
    [ObservableProperty] private ObservableCollection<AppIdInfo> _appIds = new();
    [ObservableProperty] private AppIdInfo? _selectedAppId;
    [ObservableProperty] private bool _isQuerying = false;
    [ObservableProperty] private string _operationStatus = "";

    public Func<string, Task>? CopyToClipboard { get; set; }

    public AppIdViewModel(EcoService ecoService, AuthService authService)
    {
        _ecoService = ecoService;
        _authService = authService;
    }

    [RelayCommand]
    private async Task QueryAppIdAsync()
    {
        if (string.IsNullOrEmpty(QueryPackageName))
        {
            OperationStatus = "请输入应用包名";
            return;
        }
        if (!_ecoService.IsAuthenticated)
        {
            OperationStatus = "请先登录华为账号";
            return;
        }

        try
        {
            IsQuerying = true;
            OperationStatus = "正在查询...";
            var results = await _ecoService.GetAppIdByPackageNameAsync(QueryPackageName);
            AppIds.Clear();
            foreach (var r in results) AppIds.Add(r);
            if (AppIds.Count > 0)
            {
                SelectedAppId = AppIds[0];
                OperationStatus = $"查询到 {AppIds.Count} 个AppId";
            }
            else
            {
                OperationStatus = "未查询到AppId，该包名可能尚未在AGC创建应用。请按照下方步骤在AGC创建。";
            }
        }
        catch (Exception ex)
        {
            OperationStatus = $"查询失败: {ex.Message}";
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private void OpenCreateAppUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://developer.huawei.com/consumer/cn/service/josp/agc/index.html",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            OperationStatus = $"无法打开浏览器: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyAppIdAsync()
    {
        if (SelectedAppId == null)
        {
            OperationStatus = "请先选择一条记录";
            return;
        }
        try
        {
            if (CopyToClipboard != null)
            {
                await CopyToClipboard(SelectedAppId.AppId);
                OperationStatus = $"已复制AppId: {SelectedAppId.AppId}";
            }
            else
            {
                OperationStatus = $"AppId: {SelectedAppId.AppId}（剪贴板不可用）";
            }
        }
        catch (Exception ex)
        {
            OperationStatus = $"复制失败: {ex.Message}";
        }
    }
}
