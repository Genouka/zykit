using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Models;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class PairingViewModel : ViewModelBase
{
    private readonly PairingService _pairingService;
    private readonly EcoService _ecoService;
    private readonly LocalCacheService _cacheService;
    private readonly AuthService _authService;

    [ObservableProperty] private ObservableCollection<SigningPair> _pairs = new();
    [ObservableProperty] private SigningPair? _selectedPair;
    [ObservableProperty] private ObservableCollection<SigningPair> _selectedPairs = new();
    [ObservableProperty] private string _operationStatus = "";

    // 编辑搭配
    [ObservableProperty] private bool _isEditing = false;
    [ObservableProperty] private string _editPackageName = "";
    [ObservableProperty] private string _editAppId = "";
    [ObservableProperty] private string _editCertName = "";
    [ObservableProperty] private string _editProfileName = "";
    [ObservableProperty] private string _editKeystoreName = "";
    [ObservableProperty] private string _editKeystorePassword = "";
    [ObservableProperty] private string _editKeyAlias = "";

    public PairingViewModel(PairingService pairingService, EcoService ecoService,
        LocalCacheService cacheService, AuthService authService)
    {
        _pairingService = pairingService;
        _ecoService = ecoService;
        _cacheService = cacheService;
        _authService = authService;

        _authService.LoginSuccess += (AuthInfo _) => { Refresh(); };
        Refresh();
    }

    private void Refresh()
    {
        Pairs.Clear();
        foreach (var p in _pairingService.GetAllPairs())
            Pairs.Add(p);
        OperationStatus = $"共 {Pairs.Count} 个搭配";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Refresh();
    }

    [RelayCommand]
    private void DeletePair()
    {
        if (SelectedPair == null) return;
        _pairingService.DeletePair(SelectedPair.Id);
        Refresh();
        OperationStatus = $"已删除搭配: {SelectedPair.PackageName}";
    }

    [RelayCommand]
    private void DeleteSelectedPairs()
    {
        if (SelectedPairs.Count == 0) return;
        foreach (var p in SelectedPairs.ToList())
            _pairingService.DeletePair(p.Id);
        var count = SelectedPairs.Count;
        Refresh();
        OperationStatus = $"已删除 {count} 个搭配";
    }

    [RelayCommand]
    private void ClearExpiredPairs()
    {
        _pairingService.ClearExpiredPairs();
        Refresh();
        OperationStatus = "已清理过期搭配";
    }

    [RelayCommand]
    private void StartEditPair()
    {
        if (SelectedPair == null) return;
        IsEditing = true;
        EditPackageName = SelectedPair.PackageName;
        EditAppId = SelectedPair.AppId;
        EditCertName = SelectedPair.CertName;
        EditProfileName = SelectedPair.ProfileName;
        EditKeystoreName = SelectedPair.KeystoreName;
        EditKeystorePassword = SelectedPair.KeystorePassword;
        EditKeyAlias = SelectedPair.KeyAlias;
    }

    [RelayCommand]
    private void SaveEditPair()
    {
        if (SelectedPair == null) return;
        SelectedPair.PackageName = EditPackageName;
        SelectedPair.AppId = EditAppId;
        SelectedPair.CertName = EditCertName;
        SelectedPair.ProfileName = EditProfileName;
        SelectedPair.KeystoreName = EditKeystoreName;
        SelectedPair.KeystorePassword = EditKeystorePassword;
        SelectedPair.KeyAlias = EditKeyAlias;
        _pairingService.SavePair(SelectedPair);
        IsEditing = false;
        Refresh();
        OperationStatus = "搭配已更新";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void TouchPair()
    {
        if (SelectedPair == null) return;
        _pairingService.TouchPair(SelectedPair.Id);
        Refresh();
    }
}
