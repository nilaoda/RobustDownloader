using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RobustDownloader.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "RobustDownloader.SingleInstance";
    private const string PipeName = "RobustDownloader.SingleInstance.Activate";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private Action? _activate;
    private Task? _listenTask;
    private bool _disposed;

    private SingleInstanceService(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceService? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
            return new SingleInstanceService(mutex);

        mutex.Dispose();
        NotifyPrimaryInstance();
        return null;
    }

    public void Start(Action activate)
    {
        _activate = activate;
        _listenTask ??= Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(_cts.Token);
                Dispatcher.UIThread.Post(() => _activate?.Invoke());
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private static void NotifyPrimaryInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(750);
            client.WriteByte(1);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
