using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Zykit.App.Services;

/// <summary>
/// 单实例服务：使用 Mutex 检测单实例，通过命名管道在实例间传递 hokit:// 协议 URL。
/// </summary>
public class SingleInstanceService
{
    private const string MutexName = "Zykit_App_SingleInstance_Mutex_3F7A2E";
    private const string PipeName = "Zykit_App_SingleInstance_Pipe_3F7A2E";

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    /// <summary>是否是第一个实例（持有 Mutex）</summary>
    public bool IsFirstInstance { get; private set; }

    /// <summary>当从其他实例收到消息时触发（在后台线程，订阅方需自行切到 UI 线程）</summary>
    public event Action<string>? MessageReceived;

    /// <summary>尝试获取单实例锁。返回 true 表示是第一个实例。</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
        if (!createdNew)
        {
            // 不是第一个实例，释放持有的引用
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }
        return createdNew;
    }

    /// <summary>向已运行的第一个实例发送消息（用于第二个实例传递 hokit:// URL）。</summary>
    public void SendToFirstInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000); // 3 秒超时
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
        }
        catch
        {
            /* 第一个实例可能未响应，忽略 */
        }
    }

    /// <summary>启动命名管道服务器，监听后续实例发送的消息。</summary>
    public void StartServer()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(ct);
                server.Dispose();
                server = null;

                if (!string.IsNullOrEmpty(message))
                {
                    MessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                /* 单次监听异常不应终止服务器 */
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;

        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
