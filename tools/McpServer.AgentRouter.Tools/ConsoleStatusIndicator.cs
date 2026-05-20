using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal interface IChatConsoleStatusIndicatorFactory
{
    IChatConsoleStatusIndicator Create(string message);
}

internal interface IChatConsoleStatusIndicator : IAsyncDisposable
{
    void Stop();
}

internal sealed class TerminalChatConsoleStatusIndicatorFactory : IChatConsoleStatusIndicatorFactory
{
    private readonly TextWriter _output;
    private string? _statusWindowStatePath;
    private bool _statusWindowInitialized;

    public TerminalChatConsoleStatusIndicatorFactory(TextWriter output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public IChatConsoleStatusIndicator Create(string message)
    {
        if (EnsureStatusWindowInitialized() && !string.IsNullOrWhiteSpace(_statusWindowStatePath))
        {
            return new StatusWindowChatConsoleStatusIndicator(_statusWindowStatePath, message);
        }

        return new TerminalChatConsoleStatusIndicator(_output, message);
    }

    private bool EnsureStatusWindowInitialized()
    {
        if (!ReferenceEquals(_output, Console.Out) || Console.IsOutputRedirected || !OperatingSystem.IsWindows())
        {
            _statusWindowInitialized = true;
            return false;
        }

        if (_statusWindowInitialized)
        {
            return !string.IsNullOrWhiteSpace(_statusWindowStatePath);
        }

        _statusWindowInitialized = true;
        if (TryInitializeStatusWindow(out _statusWindowStatePath))
        {
            return true;
        }

        _statusWindowStatePath = null;
        return false;
    }

    private static bool TryInitializeStatusWindow(out string? statusWindowStatePath)
    {
        statusWindowStatePath = null;

        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return false;
        }

        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return false;
        }

        var normalizedAssemblyLocation = Path.GetFullPath(assemblyLocation);
        var statePath = Path.Combine(
            Path.GetTempPath(),
            $"mcpserver-chat-status-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(statePath, string.Empty);
            if (!TryLaunchStatusWindow(normalizedAssemblyLocation, statePath))
            {
                SafeDelete(statePath);
                return false;
            }

            statusWindowStatePath = statePath;
            return true;
        }
        catch
        {
            SafeDelete(statePath);
            return false;
        }
    }

    private static bool TryLaunchStatusWindow(string assemblyLocation, string statePath)
    {
        var command = ChatConsoleStatusWindowCommandBuilder.BuildCommandLine(assemblyLocation, statePath);
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"McpServer Chat Status\" " + command,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(assemblyLocation) ?? Environment.CurrentDirectory
            });

            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal sealed class TerminalChatConsoleStatusIndicator : IChatConsoleStatusIndicator
{
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private readonly TextWriter _output;
    private readonly string _message;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task? _loopTask;
    private readonly bool _enabled;
    private readonly int _statusWidth;
    private int _frameIndex;
    private bool _stopped;

    public TerminalChatConsoleStatusIndicator(TextWriter output, string message)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _message = string.IsNullOrWhiteSpace(message) ? "Working..." : message;
        _enabled = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        _statusWidth = Math.Max(16, _message.Length + 4);

        if (_enabled)
        {
            _loopTask = Task.Run(RunAsync);
        }
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _cancellation.Cancel();

        if (_enabled)
        {
            ClearLine();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                WriteFrame(SpinnerFrames[_frameIndex]);
                _frameIndex = (_frameIndex + 1) % SpinnerFrames.Length;

                await Task.Delay(120, _cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WriteFrame(string frame)
    {
        _output.Write('\r');
        _output.Write(frame);
        _output.Write(' ');
        _output.Write(_message);
        PadToWidth(frame.Length + 1 + _message.Length);
        _output.Flush();
    }

    private void ClearLine()
    {
        if (!_enabled)
        {
            return;
        }

        _output.Write('\r');
        _output.WriteRepeated(' ', _statusWidth);
        _output.Write('\r');
        _output.Flush();
    }

    private void PadToWidth(int contentWidth)
    {
        var padding = Math.Max(0, _statusWidth - contentWidth);
        if (padding > 0)
        {
            _output.WriteRepeated(' ', padding);
        }
    }
}

internal sealed class StatusWindowChatConsoleStatusIndicator : IChatConsoleStatusIndicator
{
    private readonly string _statePath;
    private readonly string _message;
    private bool _stopped;

    public StatusWindowChatConsoleStatusIndicator(string statePath, string message)
    {
        _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
        _message = string.IsNullOrWhiteSpace(message) ? "Waiting..." : message;
        WriteState(_message);
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        WriteState(string.Empty);
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    private void WriteState(string state)
    {
        try
        {
            File.WriteAllText(_statePath, state);
        }
        catch
        {
        }
    }
}

internal static class ChatConsoleStatusWindowCommandBuilder
{
    public static string BuildCommandLine(string assemblyLocation, string statePath)
    {
        if (string.IsNullOrWhiteSpace(assemblyLocation) || string.IsNullOrWhiteSpace(statePath))
        {
            return string.Empty;
        }

        var trimmedAssembly = Path.GetFullPath(assemblyLocation);
        var trimmedStatePath = Path.GetFullPath(statePath);

        if (trimmedAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return Quote("dotnet") + " " +
                   Quote(trimmedAssembly) + " " +
                   Quote("chat-status") + " " +
                   Quote("--status-lock") + " " +
                   Quote(trimmedStatePath);
        }

        return Quote(trimmedAssembly) + " " +
               Quote("chat-status") + " " +
               Quote("--status-lock") + " " +
               Quote(trimmedStatePath);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
