using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zykit.App.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = ""; // Info, Warn, Error, Network
    public string Category { get; set; } = ""; // Operation, Request, Response
    public string Message { get; set; } = "";
    public string DisplayTimestamp => Timestamp.ToString("HH:mm:ss.fff");
    public string DisplayLevel => Level switch
    {
        "Info" => "ℹ",
        "Warn" => "⚠",
        "Error" => "✗",
        "Network" => "🌐",
        _ => Level
    };
}

public class LogService
{
    private readonly ObservableCollection<LogEntry> _entries = new();
    private const int MaxEntries = 2000;

    public ObservableCollection<LogEntry> Entries => _entries;

    public event Action<LogEntry>? EntryAdded;

    public void Info(string message, string category = "Operation")
    {
        AddEntry("Info", category, message);
    }

    public void Warn(string message, string category = "Operation")
    {
        AddEntry("Warn", category, message);
    }

    public void Error(string message, string category = "Operation")
    {
        AddEntry("Error", category, message);
    }

    public void LogRequest(string method, string url, string? body = null)
    {
        var sanitizedUrl = SanitizeUrl(url);
        var msg = $"{method} {sanitizedUrl}";
        if (!string.IsNullOrEmpty(body))
        {
            var sanitizedBody = SanitizeBody(body);
            msg += $"\nBody: {sanitizedBody}";
        }
        AddEntry("Network", "Request", msg);
    }

    public void LogResponse(string method, string url, int statusCode, string? body = null)
    {
        var sanitizedUrl = SanitizeUrl(url);
        var msg = $"{statusCode} {method} {sanitizedUrl}";
        if (!string.IsNullOrEmpty(body))
        {
            var sanitizedBody = SanitizeBody(body);
            if (sanitizedBody.Length > 500)
                sanitizedBody = sanitizedBody[..500] + "...(truncated)";
            msg += $"\nResponse: {sanitizedBody}";
        }
        AddEntry("Network", "Response", msg);
    }

    private void AddEntry(string level, string category, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = category,
            Message = message
        };
        _entries.Add(entry);
        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);
        EntryAdded?.Invoke(entry);
    }

    private static string SanitizeUrl(string url)
    {
        // Remove tempToken from URLs
        return Regex.Replace(url, @"tempToken=[^&]+", "tempToken=***", RegexOptions.IgnoreCase);
    }

    private static string SanitizeBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        try
        {
            var sanitized = body;
            // Hide oauth2Token, accessToken, jwtToken values
            sanitized = Regex.Replace(sanitized, @"""oauth2Token""\s*:\s*""[^""]*""", @"""oauth2Token"":""***""", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"""accessToken""\s*:\s*""[^""]*""", @"""accessToken"":""***""", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"""jwtToken""\s*:\s*""[^""]*""", @"""jwtToken"":""***""", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"""csr""\s*:\s*""[^""]*""", @"""csr"":""***""", RegexOptions.IgnoreCase);
            return sanitized;
        }
        catch { return body; }
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public string ExportAsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Zykit Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();
        foreach (var entry in _entries)
        {
            sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] [{entry.Category}] {entry.Message}");
        }
        return sb.ToString();
    }
}
