using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zykit.App.Json;
using Zykit.App.Models;

namespace Zykit.App.Services;

public class AuthService
{
    private readonly EcoService _ecoService;
    private readonly ISdkPathProvider _sdk;
    private AuthInfo? _currentAuth;

    public event Action<AuthInfo>? LoginSuccess;
    public event Action? LogoutSuccess;

    public AuthService(EcoService ecoService, ISdkPathProvider sdk)
    {
        _ecoService = ecoService;
        _sdk = sdk;
    }

    public AuthInfo? CurrentAuth => _currentAuth;
    public bool IsLoggedIn => _currentAuth != null && !string.IsNullOrEmpty(_currentAuth.AccessToken) && !_currentAuth.IsExpired;

    /// <summary>启动时自动加载已保存的登录信息</summary>
    public async Task<bool> TryAutoLoginAsync()
    {
        var authFile = Path.Combine(_sdk.ConfigDir, "ds-authInfo.json");
        if (!File.Exists(authFile)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(authFile);
            _currentAuth = JsonSerializer.Deserialize(json, ZykitJsonContext.Default.AuthInfo);
            if (_currentAuth == null || _currentAuth.IsExpired)
            {
                _currentAuth = null;
                if (File.Exists(authFile)) File.Delete(authFile);
                return false;
            }

            // 验证token是否仍然有效
            _ecoService.InitAuth(_currentAuth);
            try
            {
                await _ecoService.GetUserTeamListAsync();
                LoginSuccess?.Invoke(_currentAuth);
                return true;
            }
            catch
            {
                // Token已失效
                _currentAuth = null;
                if (File.Exists(authFile)) File.Delete(authFile);
                return false;
            }
        }
        catch
        {
            _currentAuth = null;
            return false;
        }
    }

    public async Task<AuthInfo> LoginAsync()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var tcs = new TaskCompletionSource<string>();

        var listener = new HttpListener();
        var port = FindFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var loginUrl = $"https://cn.devecostudio.huawei.com/console/DevEcoIDE/apply?port={port}&appid=1007&code=20698961dd4f420c8b44f49010c6f0cc";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(loginUrl) { UseShellExecute = true });

        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var ctx = await listener.GetContextAsync();
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/callback")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                    var tempToken = (await reader.ReadToEndAsync()).Trim();

                    var html = @"<!DOCTYPE html>
<html lang='zh-CN'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>登录成功</title>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { height: 100%; }
  body {
    background: linear-gradient(135deg, #0f1419 0%, #1a1f2e 50%, #0d1117 100%);
    color: #fff;
    font-family: 'Segoe UI', 'Microsoft YaHei', -apple-system, sans-serif;
    display: flex;
    justify-content: center;
    align-items: center;
    overflow: hidden;
    position: relative;
  }
  body::before {
    content: '';
    position: absolute;
    width: 700px; height: 700px;
    background: radial-gradient(circle, rgba(0, 149, 246, 0.18) 0%, transparent 70%);
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    animation: pulse 3s ease-in-out infinite;
    pointer-events: none;
  }
  body::after {
    content: '';
    position: absolute;
    width: 400px; height: 400px;
    background: radial-gradient(circle, rgba(0, 212, 170, 0.12) 0%, transparent 70%);
    top: 30%; left: 70%;
    animation: pulse 4s ease-in-out infinite reverse;
    pointer-events: none;
  }
  @keyframes pulse {
    0%, 100% { transform: translate(-50%, -50%) scale(1); opacity: 0.5; }
    50% { transform: translate(-50%, -50%) scale(1.25); opacity: 0.9; }
  }
  .card {
    background: rgba(255, 255, 255, 0.05);
    backdrop-filter: blur(24px);
    -webkit-backdrop-filter: blur(24px);
    border: 1px solid rgba(255, 255, 255, 0.1);
    border-radius: 24px;
    padding: 60px 56px;
    text-align: center;
    box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5), inset 0 1px 0 rgba(255, 255, 255, 0.08);
    animation: cardIn 0.7s cubic-bezier(0.34, 1.56, 0.64, 1);
    position: relative;
    z-index: 1;
    min-width: 360px;
  }
  @keyframes cardIn {
    0% { opacity: 0; transform: scale(0.85) translateY(24px); }
    100% { opacity: 1; transform: scale(1) translateY(0); }
  }
  .checkmark-wrap {
    width: 96px; height: 96px;
    margin: 0 auto 28px;
    position: relative;
  }
  .checkmark-circle {
    width: 96px; height: 96px;
    border-radius: 50%;
    border: 3px solid #00d4aa;
    box-shadow: 0 0 32px rgba(0, 212, 170, 0.45), inset 0 0 20px rgba(0, 212, 170, 0.1);
    animation: circleAnim 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
  }
  @keyframes circleAnim {
    0% { transform: scale(0); opacity: 0; }
    100% { transform: scale(1); opacity: 1; }
  }
  .checkmark {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    width: 48px; height: 48px;
  }
  .checkmark path {
    fill: none;
    stroke: #00d4aa;
    stroke-width: 4;
    stroke-linecap: round;
    stroke-linejoin: round;
    stroke-dasharray: 60;
    stroke-dashoffset: 60;
    animation: drawCheck 0.5s 0.45s ease-in-out forwards;
    filter: drop-shadow(0 0 6px rgba(0, 212, 170, 0.6));
  }
  @keyframes drawCheck { to { stroke-dashoffset: 0; } }
  h1 {
    font-size: 26px;
    font-weight: 600;
    margin-bottom: 12px;
    background: linear-gradient(135deg, #00d4aa 0%, #0095f6 100%);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
    background-clip: text;
    animation: textIn 0.5s 0.7s ease-out backwards;
  }
  p {
    font-size: 14px;
    opacity: 0;
    line-height: 1.7;
    color: rgba(255, 255, 255, 0.65);
    animation: textIn 0.5s 0.9s ease-out forwards;
  }
  @keyframes textIn {
    from { opacity: 0; transform: translateY(10px); }
    to { opacity: 1; transform: translateY(0); }
  }
  p { animation-fill-mode: forwards; }
  .brand {
    position: fixed;
    bottom: 28px; left: 50%;
    transform: translateX(-50%);
    font-size: 12px;
    opacity: 0;
    letter-spacing: 3px;
    color: rgba(255, 255, 255, 0.35);
    animation: textIn 0.5s 1.1s ease-out forwards;
  }
  .brand span { color: #0095f6; font-weight: 600; }
</style>
</head>
<body>
  <div class='card'>
    <div class='checkmark-wrap'>
      <div class='checkmark-circle'></div>
      <svg class='checkmark' viewBox='0 0 24 24'>
        <path d='M5 13l4 4L19 7'/>
      </svg>
    </div>
    <h1>登录成功</h1>
    <p>已成功登录华为开发者账号<br>请关闭此窗口，返回织月工具箱</p>
  </div>
  <div class='brand'><span>ZYKIT</span> · 织月工具箱</div>
</body>
</html>";
                    var buffer = Encoding.UTF8.GetBytes(html);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.OutputStream.WriteAsync(buffer);
                    ctx.Response.Close();

                    tcs.TrySetResult(tempToken);
                    break;
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
            }
        }, cts.Token);

        var tempTokenResult = await tcs.Task;
        listener.Close();

        var authInfo = await _ecoService.GetTokenByTempTokenAsync(tempTokenResult);
        authInfo.LoginTime = DateTime.UtcNow;
        authInfo.ExpiresIn = 86400;
        _currentAuth = authInfo;
        _ecoService.InitAuth(authInfo);

        // 持久化登录
        var authFile = Path.Combine(_sdk.ConfigDir, "ds-authInfo.json");
        await File.WriteAllTextAsync(authFile, JsonSerializer.Serialize(authInfo, ZykitJsonContext.Default.AuthInfo));

        LoginSuccess?.Invoke(authInfo);
        return authInfo;
    }

    public void Logout()
    {
        _currentAuth = null;
        var authFile = Path.Combine(_sdk.ConfigDir, "ds-authInfo.json");
        if (File.Exists(authFile)) File.Delete(authFile);
        LogoutSuccess?.Invoke();
    }

    private static int FindFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
