using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Zykit.App.Helpers;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class SigningView : UserControl
{
    public SigningView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SigningViewModel>();
        CertGrid.SelectionChanged += CertGrid_SelectionChanged;
    }

    private void ToggleKeystorePassword_Click(object? sender, RoutedEventArgs e)
    {
        if (KeystorePasswordBox.PasswordChar == '*')
        {
            KeystorePasswordBox.PasswordChar = '\0';
            ToggleKeystorePasswordBtn.Content = "隐藏";
        }
        else
        {
            KeystorePasswordBox.PasswordChar = '*';
            ToggleKeystorePasswordBtn.Content = "显示";
        }
    }

    private void ToggleCertKeystorePassword_Click(object? sender, RoutedEventArgs e)
    {
        if (CertKeystorePasswordBox.PasswordChar == '*')
        {
            CertKeystorePasswordBox.PasswordChar = '\0';
            ToggleCertKeystorePasswordBtn.Content = "隐藏";
        }
        else
        {
            CertKeystorePasswordBox.PasswordChar = '*';
            ToggleCertKeystorePasswordBtn.Content = "显示";
        }
    }

    private void CertGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SigningViewModel vm && vm.SelectedCert != null)
        {
            // Double-click on cert: download if cloud-only, or show details
            if (vm.SelectedCert.IsCloud && !vm.SelectedCert.IsLocal)
                vm.DownloadCertCommand.Execute(null);
        }
    }

    private void CertGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SigningViewModel vm)
        {
            vm.SelectedCerts.Clear();
            foreach (var item in CertGrid.SelectedItems)
            {
                if (item is UnifiedCertItem cert)
                    vm.SelectedCerts.Add(cert);
            }
        }
    }

    private void CopySelected_Click(object? sender, RoutedEventArgs e)
        => DataGridCopyHelper.CopySelectedRows(CertGrid);

    private void CopyCell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string propName && !string.IsNullOrEmpty(propName))
            DataGridCopyHelper.CopyCellValue(CertGrid, propName);
    }
}
