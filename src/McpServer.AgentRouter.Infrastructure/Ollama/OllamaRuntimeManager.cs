using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Infrastructure.Ollama;

public sealed class OllamaRuntimeManager : ILocalModelRuntimeManager, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaRuntimeManager> _logger;
    private readonly object _syncRoot = new();

    private Process? _managedProcess;
    private bool _disposed;

    public OllamaRuntimeManager(
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaRuntimeManager> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask EnsureAvailableAsync(
        LocalModelRuntimeStartupSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();

        var baseUri = CreateBaseUri(settings.BaseUrl);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.StartupTimeoutSeconds, 1, 600));
        var pollInterval = TimeSpan.FromMilliseconds(Math.Clamp(settings.PollIntervalMilliseconds, 100, 10000));

        if (!await TryIsOllamaReadyAsync(baseUri, cancellationToken).ConfigureAwait(false))
        {
            if (!settings.StartRuntimeIfMissing)
            {
                throw new InvalidOperationException($"Ollama is not reachable at {baseUri} and startup is not allowed to start it.");
            }

            StartManagedOllama(settings);
        }

        await WaitForOllamaAsync(baseUri, timeout, pollInterval, cancellationToken).ConfigureAwait(false);
        await EnsureRequiredModelsAsync(settings, baseUri, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopManagedRuntimeAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (_syncRoot)
        {
            process = _managedProcess;
            _managedProcess = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            _logger.LogInformation("Stopping managed Ollama process {ProcessId}.", process.Id);

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Process exited between checks.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed while stopping managed Ollama process.");
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_syncRoot)
        {
            _managedProcess?.Dispose();
            _managedProcess = null;
        }
    }

    private async Task EnsureRequiredModelsAsync(
        LocalModelRuntimeStartupSettings settings,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        var requiredModels = settings.RequiredModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requiredModels.Length == 0)
        {
            return;
        }

        var installedModels = await GetInstalledModelNamesAsync(baseUri, cancellationToken).ConfigureAwait(false);

        foreach (var model in requiredModels)
        {
            if (installedModels.Contains(model))
            {
                _logger.LogInformation("Required Ollama model is available: {Model}", model);
                continue;
            }

            if (!settings.PullMissingModels)
            {
                throw new InvalidOperationException(
                    $"Required Ollama model '{model}' is not installed. Run 'ollama pull {model}' or enable AgentRouter:Startup:PullMissingModels.");
            }

            await PullModelAsync(settings, model, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ISet<string>> GetInstalledModelNamesAsync(
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("agent-router-ollama");
        client.BaseAddress = baseUri;

        var tags = await client.GetFromJsonAsync<OllamaTagsResponse>(
                "/api/tags",
                cancellationToken)
            .ConfigureAwait(false);

        var models = tags?.Models ?? [];
        return new System.Collections.Generic.HashSet<string>(
            models
                .Where(model => !string.IsNullOrWhiteSpace(model.Name))
                .Select(model => model.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task PullModelAsync(
        LocalModelRuntimeStartupSettings settings,
        string model,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pulling missing Ollama model {Model}.", model);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.StartupTimeoutSeconds, 30, 3600)));

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("pull");
        startInfo.ArgumentList.Add(model);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = false
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start 'ollama pull {model}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'ollama pull {model}' failed with exit code {process.ExitCode}. stdout: {Truncate(stdout, 4000)} stderr: {Truncate(stderr, 4000)}");
        }

        _logger.LogInformation("Pulled Ollama model {Model}.", model);
    }

    private void StartManagedOllama(LocalModelRuntimeStartupSettings settings)
    {
        lock (_syncRoot)
        {
            if (_managedProcess is not null && !_managedProcess.HasExited)
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = settings.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("serve");

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = false
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("Failed to start managed Ollama process.");
            }

            _managedProcess = process;

            _logger.LogInformation(
                "Started managed Ollama process {ProcessId} using {ExecutablePath}.",
                process.Id,
                settings.ExecutablePath);
        }
    }

    private async Task WaitForOllamaAsync(
        Uri baseUri,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await TryIsOllamaReadyAsync(baseUri, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogInformation("Ollama is ready at {BaseUri}.", baseUri);
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastException = ex;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Ollama did not become ready at {baseUri} within {timeout.TotalSeconds:N0} seconds.",
            lastException);
    }


    private async Task<bool> TryIsOllamaReadyAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        try
        {
            return await IsOllamaReadyAsync(baseUri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> IsOllamaReadyAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("agent-router-ollama");
        client.BaseAddress = baseUri;

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(TimeSpan.FromSeconds(3));

        using var response = await client.GetAsync("/api/tags", requestCts.Token).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    private static Uri CreateBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid Ollama base URL: {value}");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported Ollama base URL scheme: {uri.Scheme}");
        }

        return uri;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public IList<OllamaModelInfo> Models { get; set; } = [];
    }

    private sealed class OllamaModelInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
