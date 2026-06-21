using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zykit.App.Services;

namespace Zykit.App.ViewModels;

public partial class LogViewModel : ViewModelBase
{
    private readonly LogService _logService;

    [ObservableProperty] private ObservableCollection<LogEntry> _entries = new();
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _showNetwork = true;
    [ObservableProperty] private bool _showOperation = true;
    [ObservableProperty] private string _operationStatus = "";

    public LogViewModel(LogService logService)
    {
        _logService = logService;
        // Load existing entries
        foreach (var e in _logService.Entries)
            Entries.Add(e);
        _logService.EntryAdded += OnEntryAdded;
    }

    private void OnEntryAdded(LogEntry entry)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            OnPropertyChanged(nameof(FilteredEntries));
        });
    }

    public IEnumerable<LogEntry> FilteredEntries
    {
        get
        {
            var query = Entries.AsEnumerable();
            if (!ShowNetwork)
                query = query.Where(e => e.Category != "Request" && e.Category != "Response");
            if (!ShowOperation)
                query = query.Where(e => e.Category == "Request" || e.Category == "Response");
            if (!string.IsNullOrEmpty(FilterText))
                query = query.Where(e => e.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                    || e.Category.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
            return query;
        }
    }

    partial void OnFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredEntries));
    partial void OnShowNetworkChanged(bool value) => OnPropertyChanged(nameof(FilteredEntries));
    partial void OnShowOperationChanged(bool value) => OnPropertyChanged(nameof(FilteredEntries));

    [RelayCommand]
    private void ClearLog()
    {
        _logService.Clear();
        Entries.Clear();
        OperationStatus = "日志已清空";
    }

    [RelayCommand]
    private async Task ExportLogAsync()
    {
        try
        {
            var text = _logService.ExportAsText();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"zykit-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, text);
            OperationStatus = $"日志已导出到: {path}";
        }
        catch (Exception ex)
        {
            OperationStatus = $"导出失败: {ex.Message}";
        }
    }
}
