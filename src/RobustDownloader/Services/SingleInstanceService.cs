using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RobustDownloader.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "RobustDownloader.SingleInstance";
    private const string PipeName = "RobustDownloader.SingleInstance.Activate";

    private readonly Mutex? _mutex;
    private readonly FileStream? _lockFile;
    private readonly CancellationTokenSource _cts = new();
    private Action<CommandLineCommand>? _handleCommand;
    private Task? _listenTask;
    private bool _disposed;

    private SingleInstanceService(Mutex? mutex, FileStream? lockFile)
    {
        _mutex = mutex;
        _lockFile = lockFile;
    }

    public static SingleInstanceService? TryAcquire(CommandLineCommand command, out bool commandSent)
    {
        var mutex = new Mutex(true, MutexName, out var mutexCreated);
        var lockFile = TryAcquireLockFile();

        if (mutexCreated && lockFile != null)
        {
            commandSent = false;
            return new SingleInstanceService(mutex, lockFile);
        }

        lockFile?.Dispose();
        mutex.Dispose();
        commandSent = NotifyPrimaryInstance(command.Kind == CommandLineCommandKind.None ? CommandLineCommand.Show() : command);
        return null;
    }

    public void Start(Action<CommandLineCommand> handleCommand)
    {
        _handleCommand = handleCommand;
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
                var command = await ReadCommandAsync(server, _cts.Token);
                Dispatcher.UIThread.Post(() => _handleCommand?.Invoke(command));
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

    private static async Task<CommandLineCommand> ReadCommandAsync(Stream stream, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            var json = await reader.ReadToEndAsync(token);
            if (string.IsNullOrWhiteSpace(json))
                return CommandLineCommand.Show();

            return JsonSerializer.Deserialize(json, AppJsonContext.Default.CommandLineCommand)
                   ?? CommandLineCommand.Show();
        }
        catch
        {
            return CommandLineCommand.Show();
        }
    }

    private static bool NotifyPrimaryInstance(CommandLineCommand command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(750);
            JsonSerializer.Serialize(client, command, AppJsonContext.Default.CommandLineCommand);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _lockFile?.Dispose();
        if (_mutex != null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    private static FileStream? TryAcquireLockFile()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var lockPath = Path.Combine(AppPaths.DataDirectory, "single-instance.lock");
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
