using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace McpServer.IntegrationTests.Infrastructure;

public sealed class HttpTestServerProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private readonly Process _process;
    private readonly CancellationTokenSource _stdoutPumpCts = new();
    private readonly CancellationTokenSource _stderrPumpCts = new();
    private readonly Task _stdoutPumpTask;
    private readonly Task _stderrPumpTask;
    private readonly ConcurrentQueue<string> _stdoutLines = new();
    private readonly ConcurrentQueue<string> _stderrLines = new();

    public Uri BaseAddress { get; }

    public IReadOnlyCollection<string> StandardOutputLines => _stdoutLines.ToArray();

    public IReadOnlyCollection<string> StandardErrorLines => _stderrLines.ToArray();

    private HttpTestServerProcess(Process process, Uri baseAddress)
    {
        _process = process;
        BaseAddress = baseAddress;
        _stdoutPumpTask = Task.Run(() => PumpAsync(process.StandardOutput, _stdoutLines, _stdoutPumpCts.Token));
        _stderrPumpTask = Task.Run(() => PumpAsync(process.StandardError, _stderrLines, _stderrPumpCts.Token));
    }

    public static async Task<HttpTestServerProcess> StartAsync(
        string projectPath,
        Uri baseAddress,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var configuration = GetCurrentConfiguration();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -c {configuration} --no-build --no-launch-profile",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["ASPNETCORE_URLS"] = baseAddress.GetLeftPart(UriPartial.Authority);
        psi.Environment["AgentRouter__BindUrl"] = baseAddress.GetLeftPart(UriPartial.Authority);

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                psi.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start AgentRouter host process.");
        }

        var server = new HttpTestServerProcess(process, baseAddress);
        try
        {
            await server.WaitForReadyAsync(ct).ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _stdoutPumpCts.Cancel();
            _stderrPumpCts.Cancel();

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }

            await Task.WhenAll(_stdoutPumpTask, _stderrPumpTask).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _stdoutPumpCts.Dispose();
            _stderrPumpCts.Dispose();
            _process.Dispose();
        }
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(StartupTimeout);

        using var client = new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(2)
        };

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();

            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"AgentRouter host exited during startup. {GetStandardErrorSummary()}");
            }

            try
            {
                using var response = await client.GetAsync("/health", HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!timeoutCts.IsCancellationRequested)
            {
            }

            await Task.Delay(100, timeoutCts.Token).ConfigureAwait(false);
        }
    }

    private static async Task PumpAsync(
        StreamReader reader,
        ConcurrentQueue<string> lines,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                lines.Enqueue(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private string GetStandardErrorSummary()
    {
        var lines = _stderrLines.ToArray();
        if (lines.Length == 0)
        {
            return "No stderr output was captured.";
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetCurrentConfiguration()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var pathSegment = $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}";

        return baseDirectory.Contains(pathSegment, StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
    }
}
