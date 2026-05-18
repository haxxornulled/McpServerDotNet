using System.Diagnostics;
using System.Threading.Channels;
using System.Text.Json;
using Autofac;
using McpServer.Application.Abstractions.Files;
using McpServer.Protocol.Lifecycle;
using McpServer.Protocol.Notifications;
using McpServer.Protocol.Prompts;
using McpServer.Protocol.Resources;
using McpServer.Protocol.Roots;
using McpServer.Protocol.Tools;
using McpServer.Protocol;
using McpServer.Protocol.JsonRpc;
using McpServer.Protocol.Routing;
using McpServer.Protocol.Session;

namespace McpServer.Host.Transport.Stdio;

public sealed class StdioServerHostedService(
    ILifetimeScope scope,
    ILogger<StdioServerHostedService> logger,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MCP server starting (stdio)");

        await using var transport = new StdioMessageTransport(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            scope.Resolve<ILogger<StdioMessageTransport>>());

        var session = scope.Resolve<McpSession>();
        var initializeHandler = scope.Resolve<InitializeHandler>();
        var shutdownHandler = scope.Resolve<ShutdownHandler>();
        var exitHandler = scope.Resolve<ExitHandler>();
        var toolRouter = scope.Resolve<ToolCallRouter>();
        var resourceRouter = scope.Resolve<ResourceReadRouter>();
        var promptRouter = scope.Resolve<PromptRouter>();
        var pathPolicy = scope.Resolve<McpServer.Infrastructure.Files.PathPolicy>();
        var resourcePathTranslator = scope.Resolve<McpServer.Infrastructure.Files.ResourcePathTranslator>();
        var changeFeed = scope.Resolve<IWorkspaceChangeFeed>();
        var workspaceFileWatcher = scope.Resolve<IWorkspaceFileWatcher>();
        var changeChannel = Channel.CreateUnbounded<WorkspaceChangeEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        using var changeSubscription = SubscribeToWorkspaceChanges(changeFeed, changeChannel.Writer);
        var notificationPump = PumpWorkspaceChangeNotificationsAsync(changeChannel.Reader, transport, logger, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var readResult = await transport.ReadRequestAsync(stoppingToken).ConfigureAwait(false);
            if (readResult.EndOfStream)
            {
                logger.LogInformation("Stopping host because the stdio input stream closed");
                applicationLifetime.StopApplication();
                break;
            }

            var request = readResult.Request;
            if (request is null)
            {
                continue;
            }

            using var logScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["RequestId"] = request.Id?.ToString(),
                ["Method"] = request.Method
            });

            var started = Stopwatch.GetTimestamp();

            var dispatch = await DispatchAsync(
                request,
                session,
                initializeHandler,
                shutdownHandler,
                exitHandler,
                toolRouter,
                resourceRouter,
                promptRouter,
                pathPolicy,
                resourcePathTranslator,
                workspaceFileWatcher,
                transport,
                logger,
                stoppingToken).ConfigureAwait(false);

            if (dispatch.Response is not null)
            {
                await transport.WriteResponseAsync(dispatch.Response, stoppingToken).ConfigureAwait(false);
            }

            logger.LogInformation(
                "Handled {Method} in {ElapsedMs}ms",
                request.Method,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (dispatch.ShouldExit)
            {
                logger.LogInformation("Stopping host after exit notification");
                applicationLifetime.StopApplication();
                break;
            }
        }

        changeChannel.Writer.TryComplete();
        await notificationPump.ConfigureAwait(false);

        logger.LogInformation("MCP server stopping");
    }

    private static async ValueTask<DispatchResult> DispatchAsync(
        JsonRpcRequest request,
        McpSession session,
        InitializeHandler initializeHandler,
        ShutdownHandler shutdownHandler,
        ExitHandler exitHandler,
        ToolCallRouter toolRouter,
        ResourceReadRouter resourceRouter,
        PromptRouter promptRouter,
        McpServer.Infrastructure.Files.PathPolicy pathPolicy,
        McpServer.Infrastructure.Files.ResourcePathTranslator resourcePathTranslator,
        IWorkspaceFileWatcher workspaceFileWatcher,
        StdioMessageTransport transport,
        ILogger<StdioServerHostedService> logger,
        CancellationToken ct)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => DispatchResult.WithResponse(HandleInitialize(request, session, initializeHandler)),
                "ping" => DispatchResult.WithResponse(HandlePing(request)),
                "notifications/initialized" => DispatchResult.WithResponse(
                    await HandleInitializedAsync(request, session, transport, pathPolicy, resourcePathTranslator, workspaceFileWatcher, logger, ct).ConfigureAwait(false)),
                "shutdown" => DispatchResult.WithResponse(HandleShutdown(request, session, shutdownHandler)),
                "exit" => DispatchResult.Exit(HandleExit(session, exitHandler)),
                "tools/list" => DispatchResult.WithResponse(
                    RequireReady(request, session, () => new JsonRpcResponse("2.0", request.Id, toolRouter.ListTools()))),
                "tools/call" => DispatchResult.WithResponse(
                    await HandleToolCallAsync(request, session, toolRouter, ct).ConfigureAwait(false)),
                "resources/list" => DispatchResult.WithResponse(
                    RequireReady(request, session, () => new JsonRpcResponse("2.0", request.Id, resourceRouter.ListResources()))),
                "resources/read" => DispatchResult.WithResponse(
                    await HandleResourceReadAsync(request, session, resourceRouter, ct).ConfigureAwait(false)),
                "prompts/list" => DispatchResult.WithResponse(
                    RequireReady(request, session, () => new JsonRpcResponse("2.0", request.Id, promptRouter.ListPrompts()))),
                "prompts/get" => DispatchResult.WithResponse(
                    await HandlePromptGetAsync(request, session, promptRouter, ct).ConfigureAwait(false)),
                _ => DispatchResult.WithResponse(JsonRpcErrorFactory.MethodNotFound(request.Id, request.Method))
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DispatchResult.WithResponse(JsonRpcErrorFactory.InternalError(request.Id, ex.Message));
        }
    }

    private static JsonRpcResponse HandleInitialize(
        JsonRpcRequest request,
        McpSession session,
        InitializeHandler initializeHandler)
    {
        var payload = request.Params?.Deserialize<InitializeRequestDto>();
        if (payload is null)
        {
            return JsonRpcErrorFactory.InvalidParams(request.Id, "Invalid initialize params");
        }

        var result = initializeHandler.Handle(payload, session);

        return result.Match(
            Succ: x => new JsonRpcResponse("2.0", request.Id, x),
            Fail: e => JsonRpcErrorFactory.ServerError(request.Id, e.Message));
    }

    private static async ValueTask<JsonRpcResponse?> HandleInitializedAsync(
        JsonRpcRequest request,
        McpSession session,
        StdioMessageTransport transport,
        McpServer.Infrastructure.Files.PathPolicy pathPolicy,
        McpServer.Infrastructure.Files.ResourcePathTranslator resourcePathTranslator,
        IWorkspaceFileWatcher workspaceFileWatcher,
        ILogger<StdioServerHostedService> logger,
        CancellationToken ct)
    {
        var result = session.MarkReady();

        var response = result.Match<JsonRpcResponse?>(
            Succ: _ => null,
            Fail: e => JsonRpcErrorFactory.ServerError(request.Id, e.Message));

        if (response is not null || !session.SupportsRoots)
        {
            return response;
        }

        try
        {
            var rootsResponse = await transport.SendRequestAsync("roots/list", new ListRootsRequestParams(), ct).ConfigureAwait(false);
            if (rootsResponse is null)
            {
                return null;
            }

            if (rootsResponse.Error is not null)
            {
                logger.LogInformation("Client roots/list request failed: {Message}", rootsResponse.Error.Message);
                return null;
            }

            if (rootsResponse.Result is not JsonElement resultElement ||
                !resultElement.TryGetProperty("roots", out var rootsElement) ||
                rootsElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogInformation("Client roots/list response did not contain roots");
                return null;
            }

            var roots = rootsElement.Deserialize<IReadOnlyList<RootDto>>() ?? [];
            if (roots.Count == 0)
            {
                return null;
            }

            var normalizedRoots = roots
                .Select(root => new Uri(root.Uri).LocalPath)
                .Select(static path => Path.GetFullPath(path))
                .ToArray();
            var updateRootsResult = session.UpdateClientRoots(roots);
            if (updateRootsResult.IsFail)
            {
                logger.LogWarning("Failed storing client roots in session");
            }

            var configuredAllowedRoots = pathPolicy.AllowedRoots.ToArray();
            var approvedRoots = FilterClientRootsToConfiguredAllowedRoots(normalizedRoots, configuredAllowedRoots);

            if (approvedRoots.Length == 0)
            {
                logger.LogWarning(
                    "Ignored client roots because none were inside configured allowed roots. ClientRoots={ClientRoots} AllowedRoots={AllowedRoots}",
                    string.Join(", ", normalizedRoots),
                    string.Join(", ", configuredAllowedRoots));
                return null;
            }

            pathPolicy.SetAllowedRoots(approvedRoots);
            resourcePathTranslator.SetWorkspaceRoot(approvedRoots[0]);
            pathPolicy.SetProjectRoot(approvedRoots[0]);
            resourcePathTranslator.SetProjectRoot(approvedRoots[0]);
            workspaceFileWatcher.SetProjectRoot(approvedRoots[0]);

            logger.LogInformation("Configured session roots: {Roots}", string.Join(", ", approvedRoots));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to negotiate client roots");
        }

        return null;
    }

    private static JsonRpcResponse HandlePing(JsonRpcRequest request) =>
        new("2.0", request.Id, Result: new Dictionary<string, object?>());

    private static JsonRpcResponse HandleShutdown(
        JsonRpcRequest request,
        McpSession session,
        ShutdownHandler shutdownHandler)
    {
        var result = shutdownHandler.Handle(session);

        return result.Match(
            Succ: _ => new JsonRpcResponse("2.0", request.Id, new Dictionary<string, object?>()),
            Fail: e => JsonRpcErrorFactory.ServerError(request.Id, e.Message));
    }

    private static bool HandleExit(McpSession session, ExitHandler exitHandler) => exitHandler.Handle(session);

    private static async ValueTask<JsonRpcResponse> HandleToolCallAsync(
        JsonRpcRequest request,
        McpSession session,
        ToolCallRouter router,
        CancellationToken ct)
    {
        var readiness = EnsureReady(request, session);
        if (readiness is not null)
        {
            return readiness;
        }

        var payload = request.Params?.Deserialize<CallToolRequestParams>();
        if (payload is null)
        {
            return JsonRpcErrorFactory.InvalidParams(request.Id, "Invalid tools/call params");
        }

        var result = await router.RouteAsync(payload.Name, payload.Arguments, ct).ConfigureAwait(false);

        return result.Match(
            Succ: x => new JsonRpcResponse("2.0", request.Id, x),
            Fail: e => JsonRpcErrorFactory.ServerError(request.Id, e.Message));
    }

    private static async ValueTask<JsonRpcResponse> HandleResourceReadAsync(
        JsonRpcRequest request,
        McpSession session,
        ResourceReadRouter router,
        CancellationToken ct)
    {
        var readiness = EnsureReady(request, session);
        if (readiness is not null)
        {
            return readiness;
        }

        var payload = request.Params?.Deserialize<ReadResourceRequestParams>();
        if (payload is null)
        {
            return JsonRpcErrorFactory.InvalidParams(request.Id, "Invalid resources/read params");
        }

        var result = await router.RouteAsync(payload.Uri, ct).ConfigureAwait(false);

        return result.Match(
            Succ: x => new JsonRpcResponse("2.0", request.Id, x),
            Fail: e => JsonRpcErrorFactory.ServerError(request.Id, e.Message));
    }

    private static async ValueTask<JsonRpcResponse> HandlePromptGetAsync(
        JsonRpcRequest request,
        McpSession session,
        PromptRouter router,
        CancellationToken ct)
    {
        var readiness = EnsureReady(request, session);
        if (readiness is not null)
        {
            return readiness;
        }

        var payload = request.Params?.Deserialize<GetPromptRequestParams>();
        if (payload is null)
        {
            return JsonRpcErrorFactory.InvalidParams(request.Id, "Invalid prompts/get params");
        }

        var result = await router.GetAsync(payload.Name, payload.Arguments, ct).ConfigureAwait(false);

        return result.Match(
            Succ: x => new JsonRpcResponse("2.0", request.Id, x),
            Fail: e => JsonRpcErrorFactory.ServerError(request.Id, e.Message));
    }

    private static JsonRpcResponse RequireReady(
        JsonRpcRequest request,
        McpSession session,
        Func<JsonRpcResponse> next) =>
        EnsureReady(request, session) ?? next();

    private static JsonRpcResponse? EnsureReady(JsonRpcRequest request, McpSession session) =>
        session.IsReady ? null : JsonRpcErrorFactory.SessionNotReady(request.Id);

    private static IDisposable SubscribeToWorkspaceChanges(
        IWorkspaceChangeFeed changeFeed,
        ChannelWriter<WorkspaceChangeEntry> writer)
    {
        void Handler(object? sender, WorkspaceChangeEntry entry)
        {
            writer.TryWrite(entry);
        }

        changeFeed.Changed += Handler;
        return new DelegateDisposable(() => changeFeed.Changed -= Handler);
    }

    private static async Task PumpWorkspaceChangeNotificationsAsync(
        ChannelReader<WorkspaceChangeEntry> reader,
        StdioMessageTransport transport,
        ILogger<StdioServerHostedService> logger,
        CancellationToken ct)
    {
        try
        {
            await foreach (var change in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var workspaceChanged = new JsonRpcNotification(
                    "2.0",
                    "notifications/workspace/changed",
                    new WorkspaceChangeNotificationParams(
                        change.Operation,
                        change.Path,
                        change.Timestamp,
                        change.Source,
                        change.Details));

                await transport.WriteNotificationAsync(workspaceChanged, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace change notification pump stopped unexpectedly");
        }
    }

    private sealed class DelegateDisposable(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }

    private static string[] FilterClientRootsToConfiguredAllowedRoots(
        IEnumerable<string> clientRoots,
        IEnumerable<string> configuredAllowedRoots) =>
        clientRoots
            .Where(clientRoot => configuredAllowedRoots.Any(allowedRoot => IsSameOrDescendant(clientRoot, allowedRoot)))
            .Distinct(McpServer.Infrastructure.Files.PathComparison.Comparer)
            .ToArray();

    private static bool IsSameOrDescendant(string path, string allowedRoot)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedAllowedRoot = NormalizePath(allowedRoot);

        return normalizedPath.Equals(normalizedAllowedRoot, McpServer.Infrastructure.Files.PathComparison.Comparison) ||
               normalizedPath.StartsWith(
                   normalizedAllowedRoot + Path.DirectorySeparatorChar,
                   McpServer.Infrastructure.Files.PathComparison.Comparison);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private readonly record struct DispatchResult(JsonRpcResponse? Response, bool ShouldExit)
    {
        public static DispatchResult WithResponse(JsonRpcResponse? response) => new(response, false);
        public static DispatchResult Exit(bool shouldExit) => new(null, shouldExit);
    }
}
