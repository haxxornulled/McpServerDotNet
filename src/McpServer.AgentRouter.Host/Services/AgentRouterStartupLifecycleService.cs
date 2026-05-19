using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using McpServer.AgentRouter.Host.Configuration;
using Microsoft.Extensions.Options;

namespace McpServer.AgentRouter.Host.Services;

public sealed class AgentRouterStartupLifecycleService : IHostedLifecycleService
{
    private readonly IOptionsMonitor<AgentRouterOptions> _options;
    private readonly AgentRouterRuntimeSettings _runtimeSettings;
    private readonly ILocalModelRuntimeManager _modelRuntimeManager;
    private readonly IMcpToolCatalogClient _mcpToolCatalogClient;
    private readonly ISshProfileStore _sshProfileStore;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;
    private readonly ILogger<AgentRouterStartupLifecycleService> _logger;

    public AgentRouterStartupLifecycleService(
        IOptionsMonitor<AgentRouterOptions> options,
        AgentRouterRuntimeSettings runtimeSettings,
        ILocalModelRuntimeManager modelRuntimeManager,
        IMcpToolCatalogClient mcpToolCatalogClient,
        ISshProfileStore sshProfileStore,
        IAgentRouterRuntimePathResolver pathResolver,
        ILogger<AgentRouterStartupLifecycleService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runtimeSettings = runtimeSettings ?? throw new ArgumentNullException(nameof(runtimeSettings));
        _modelRuntimeManager = modelRuntimeManager ?? throw new ArgumentNullException(nameof(modelRuntimeManager));
        _mcpToolCatalogClient = mcpToolCatalogClient ?? throw new ArgumentNullException(nameof(mcpToolCatalogClient));
        _sshProfileStore = sshProfileStore ?? throw new ArgumentNullException(nameof(sshProfileStore));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var startup = _options.CurrentValue.Startup;
        if (!startup.Enabled)
        {
            _logger.LogInformation("AgentRouter startup lifecycle orchestration is disabled.");
            return;
        }

        _logger.LogInformation("AgentRouter startup lifecycle orchestration starting.");

        if (startup.EnsureRunStorageRoot)
        {
            EnsureRuntimeStorageRoots(_runtimeSettings);
        }

        if (_runtimeSettings.SshExecution.Enabled)
        {
            await LogSshProfileSourcesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (startup.EnsureOllama)
        {
            await ExecuteStartupStepAsync(
                    stepName: "Ollama runtime readiness",
                    startup,
                    action: token => _modelRuntimeManager.EnsureAvailableAsync(MapLocalModelRuntimeStartupSettings(startup), token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var startup = _options.CurrentValue.Startup;
        if (!startup.Enabled)
        {
            return;
        }

        if (!startup.VerifyMcpToolCatalogAfterStart)
        {
            _logger.LogInformation("MCP tool catalog startup verification is disabled.");
            return;
        }

        await ExecuteStartupStepAsync(
                stepName: "MCP stdio tool catalog readiness",
                startup,
                action: async token =>
                {
                    var result = await _mcpToolCatalogClient.ListToolsAsync(token).ConfigureAwait(false);
                    result.Match(
                        Succ: snapshot =>
                        {
                            _logger.LogInformation(
                                "MCP stdio tool catalog verified with {ToolCount} tools using protocol {ProtocolVersion} in {ElapsedMilliseconds}ms.",
                                snapshot.Tools.Count,
                                snapshot.ProtocolVersion,
                                snapshot.ElapsedMilliseconds);
                            return Unit.Default;
                        },
                        Fail: error => throw new InvalidOperationException(error.Message));
                },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("AgentRouter startup lifecycle orchestration completed.");
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRouter startup lifecycle service stopping.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var startup = _options.CurrentValue.Startup;
        if (!startup.Enabled || !startup.StopManagedOllamaOnShutdown)
        {
            return;
        }

        await _modelRuntimeManager.StopManagedRuntimeAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRouter startup lifecycle service stopped.");
        return Task.CompletedTask;
    }

    private async Task LogSshProfileSourcesAsync(CancellationToken cancellationToken)
    {
        var result = await _sshProfileStore.LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        result.Match(
            Succ: catalog =>
            {
                foreach (var source in catalog.Sources)
                {
                    if (!source.Enabled)
                    {
                        _logger.LogInformation(
                            "SSH profile source {SourceName} is disabled. Path: {Path}",
                            source.SourceName,
                            source.Path);
                        continue;
                    }

                    if (!source.Exists)
                    {
                        _logger.LogInformation(
                            "SSH profile source {SourceName} was not found at {Path}.",
                            source.SourceName,
                            source.Path);
                        continue;
                    }

                    _logger.LogInformation(
                        "SSH profile source {SourceName} loaded {ProfileCount} profile(s) from {Path}.",
                        source.SourceName,
                        source.ProfileCount,
                        source.Path);
                }

                var profileNames = catalog.Profiles.Keys
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (profileNames.Length == 0)
                {
                    _logger.LogInformation("No SSH profiles are currently available. SSH endpoint will deny unknown profiles until a profile file is configured.");
                }
                else
                {
                    _logger.LogInformation(
                        "SSH profiles available: {ProfileNames}",
                        string.Join(", ", profileNames));
                }

                return Unit.Default;
            },
            Fail: error =>
            {
                _logger.LogWarning(
                    "Failed to load SSH profile sources during startup: {Message}",
                    error.Message);
                return Unit.Default;
            });
    }

    private async Task ExecuteStartupStepAsync(
        string stepName,
        AgentRouterStartupOptions startup,
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Startup step beginning: {StepName}", stepName);
            await action(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Startup step completed: {StepName}", stepName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (startup.FailFastOnStartupFailure)
            {
                _logger.LogError(ex, "Startup step failed and fail-fast is enabled: {StepName}", stepName);
                throw;
            }

            _logger.LogWarning(ex, "Startup step failed but fail-fast is disabled: {StepName}", stepName);
        }
    }

    private void EnsureRuntimeStorageRoots(AgentRouterRuntimeSettings runtimeSettings)
    {
        EnsureDirectory(
            label: "durable run storage root",
            configuredPath: runtimeSettings.RunStorage.RootPath);

        if (runtimeSettings.AgentLoop.WriteTraceFiles)
        {
            EnsureDirectory(
                label: "agent loop trace root",
                configuredPath: runtimeSettings.AgentLoop.TraceRootPath);
        }

        if (runtimeSettings.ToolExecution.WriteTraceFiles)
        {
            EnsureDirectory(
                label: "MCP tool-call trace root",
                configuredPath: runtimeSettings.ToolExecution.TraceRootPath);
        }

        if (runtimeSettings.ShellExecution.WriteTraceFiles)
        {
            EnsureDirectory(
                label: "shell execution trace root",
                configuredPath: runtimeSettings.ShellExecution.TraceRootPath);
        }

        EnsureDirectory(
            label: "shell execution working-directory root",
            configuredPath: runtimeSettings.ShellExecution.WorkingDirectoryRoot);

        if (runtimeSettings.SshExecution.WriteTraceFiles)
        {
            EnsureDirectory(
                label: "SSH execution trace root",
                configuredPath: runtimeSettings.SshExecution.TraceRootPath);
        }
    }

    private void EnsureDirectory(string label, string configuredPath)
    {
        var resolvedPath = _pathResolver.ResolveRelativeToContentRoot(configuredPath);
        Directory.CreateDirectory(resolvedPath);

        _logger.LogInformation(
            "AgentRouter {StorageLabel} verified at {ResolvedPath}.",
            label,
            resolvedPath);
    }

    private static LocalModelRuntimeStartupSettings MapLocalModelRuntimeStartupSettings(AgentRouterStartupOptions startup)
    {
        return new LocalModelRuntimeStartupSettings
        {
            StartRuntimeIfMissing = startup.StartOllamaIfMissing,
            ExecutablePath = startup.OllamaExecutablePath,
            BaseUrl = startup.OllamaBaseUrl,
            StartupTimeoutSeconds = startup.StartupTimeoutSeconds,
            PollIntervalMilliseconds = startup.PollIntervalMilliseconds,
            PullMissingModels = startup.PullMissingModels,
            RequiredModels = startup.RequiredModels
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model.Trim())
                .ToArray()
        };
    }

    private readonly struct Unit
    {
        public static Unit Default { get; } = new();
    }
}
