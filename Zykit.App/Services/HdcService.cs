using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class HdcService
{
    private readonly ISdkPathProvider _sdk;

    public HdcService(ISdkPathProvider sdk)
    {
        _sdk = sdk;
    }

    private async Task<(bool success, string output)> RunCommandAsync(string args, int timeoutMs = 120000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _sdk.HdcPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                return (false, "命令执行超时");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = stdout + stderr;

            return (process.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<DeviceInfo>> GetDeviceListAsync()
    {
        var (success, output) = await RunCommandAsync("list targets");
        if (!success) return new();

        return output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && !l.Contains("[Empty]") && !l.Contains("Empty"))
            .Select(id => new DeviceInfo { DeviceId = id })
            .ToList();
    }

    public async Task<List<DeviceInfo>> GetDeviceListVerboseAsync()
    {
        var (success, output) = await RunCommandAsync("list targets -v");
        if (!success) return await GetDeviceListAsync();

        var devices = new List<DeviceInfo>();
        foreach (var line in output.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)))
        {
            // Skip [Empty] marker and lines that are just "hdc"
            if (line.StartsWith("[Empty]") || line.Equals("hdc", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var deviceId = parts[0];
                // Skip if the first token is "hdc" (not a device ID)
                if (deviceId.Equals("hdc", StringComparison.OrdinalIgnoreCase))
                    continue;

                var device = new DeviceInfo { DeviceId = deviceId };
                // 解析设备名称（第4列）: DeviceId Protocol Status Name ...
                if (parts.Length >= 4)
                    device.Name = parts[3];

                if (line.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                    device.Status = "Unauthorized";
                else if (line.Contains("Offline", StringComparison.OrdinalIgnoreCase))
                    device.Status = "Offline";
                else
                    device.Status = "Connected";
                devices.Add(device);
            }
        }
        return devices;
    }

    public async Task<(bool success, string message)> ConnectDeviceAsync(string ipPort)
    {
        var (success, output) = await RunCommandAsync($"tconn {ipPort}");
        if (!success || output.Contains("Connect failed") || output.Contains("Fail"))
            return (false, output.Trim());
        return (true, output.Trim());
    }

    public async Task<(bool success, string message)> DisconnectDeviceAsync(string ipPort)
    {
        var (success, output) = await RunCommandAsync($"tconn {ipPort} -remove");
        if (!success) return (false, $"断开失败: {output}");
        return (true, output.Trim());
    }

    public async Task<string> GetUdidAsync(string? deviceId = null)
    {
        var deviceArg = deviceId != null ? $"-t {deviceId}" : "";
        var (success, output) = await RunCommandAsync($"{deviceArg} shell bm get --udid");
        if (!success || output.Contains("Not match target founded"))
            throw new Exception($"未发现设备: {deviceId}");

        var lines = output.Trim().Split('\n');
        return lines.Length > 1 ? lines[1].Trim() : "";
    }

    public async Task<(bool success, string message)> SendFileAsync(string deviceId, string filePath)
    {
        var deviceArg = !string.IsNullOrEmpty(deviceId) ? $"-t {deviceId}" : "";

        await RunCommandAsync($"{deviceArg} shell rm -r data/local/tmp/hap");
        await RunCommandAsync($"{deviceArg} shell mkdir -p data/local/tmp/hap");

        var (success, output) = await RunCommandAsync($"{deviceArg} file send \"{filePath}\" data/local/tmp/hap/");
        if (!success) return (false, $"发送文件失败: {output}");
        if (!output.Contains("finish", StringComparison.OrdinalIgnoreCase) && !output.Contains("FileTransfer finish"))
            return (false, $"发送文件失败: {output}");
        return (true, output.Trim());
    }

    public async Task<(bool success, string message)> InstallHapAsync(string deviceId)
    {
        var deviceArg = !string.IsNullOrEmpty(deviceId) ? $"-t {deviceId}" : "";
        var (success, output) = await RunCommandAsync($"{deviceArg} shell bm install -p data/local/tmp/hap/");
        if (!success) return (false, $"安装失败: {output}");
        if (!output.Contains("successfully", StringComparison.OrdinalIgnoreCase) && !output.Contains("success", StringComparison.OrdinalIgnoreCase))
            return (false, $"安装失败: {output}");
        return (true, output.Trim());
    }

    public async Task<(bool success, string message)> SendAndInstallAsync(string filePath, string? deviceIp = null)
    {
        if (!File.Exists(filePath))
            return (false, $"文件不存在: {filePath}");

        string deviceKey;
        if (!string.IsNullOrEmpty(deviceIp))
        {
            if (deviceIp.Contains(':'))
            {
                var (connSuccess, connMsg) = await ConnectDeviceAsync(deviceIp);
                if (!connSuccess) return (false, connMsg);
            }
            deviceKey = deviceIp;
        }
        else
        {
            var devices = await GetDeviceListAsync();
            if (devices.Count == 0)
                return (false, "请连接手机，并开启开发者模式和USB调试");
            deviceKey = devices[0].DeviceId;
        }

        var (sendSuccess, sendMsg) = await SendFileAsync(deviceKey, filePath);
        if (!sendSuccess) return (false, sendMsg);

        return await InstallHapAsync(deviceKey);
    }
}
