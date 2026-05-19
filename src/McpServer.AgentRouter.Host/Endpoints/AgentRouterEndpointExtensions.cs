using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Mcp;
using McpServer.AgentRouter.Host.Configuration;
using ApiAgentLoops = McpServer.AgentRouter.Host.Protocol.AgentLoops;
using ApiAgentRuns = McpServer.AgentRouter.Host.Protocol.AgentRuns;
using ApiMcp = McpServer.AgentRouter.Host.Protocol.Mcp;
using McpServer.AgentRouter.Host.Protocol.OpenAi;
using ApiShell = McpServer.AgentRouter.Host.Protocol.Shell;
using ApiSsh = McpServer.AgentRouter.Host.Protocol.Ssh;
using McpServer.AgentRouter.Domain.AgentLoops;
using McpServer.AgentRouter.Domain.Inference;
using Microsoft.Extensions.Options;

namespace McpServer.AgentRouter.Host.Endpoints;

public static class AgentRouterEndpointExtensions
{
    public static IEndpointRouteBuilder MapAgentRouterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health", static () => Results.Ok(new
        {
            status = "ok",
            service = "McpServer.AgentRouter",
            timestampUtc = DateTimeOffset.UtcNow
        }));

        endpoints.MapGet("/v1/models", static (IModelProfileResolver resolver) =>
        {
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var response = new OpenAiModelListResponse();

            foreach (var profile in resolver.ListProfiles())
            {
                response.Data.Add(new OpenAiModelDescriptor
                {
                    Id = profile.Name,
                    Created = created,
                    OwnedBy = profile.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                        ? "local"
                        : profile.Provider
                });
            }

            return Results.Ok(response);
        });

        endpoints.MapPost(
            "/v1/chat/completions",
            static async Task<IResult> (
                OpenAiChatCompletionRequest? request,
                IOptionsMonitor<AgentRouterOptions> options,
                IModelRouter router,
                CancellationToken cancellationToken) =>
            {
                var mapped = MapChatCompletionRequest(request, options.CurrentValue);
                if (mapped.IsFail)
                {
                    return ToRequestMappingFailure(mapped);
                }

                var invocation = mapped.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected request mapping failure."));

                if (request?.Stream == true)
                {
                    var streamResult = await router.StreamAsync(invocation, cancellationToken).ConfigureAwait(false);
                    if (streamResult.IsFail)
                    {
                        return ToModelRouterFailure(streamResult);
                    }

                    var stream = streamResult.Match(
                        Succ: value => value,
                        Fail: _ => throw new InvalidOperationException("Unexpected streaming model router failure."));

                    return new OpenAiChatCompletionStreamResult(invocation.ModelProfileName, stream.Chunks);
                }

                var result = await router.CompleteAsync(invocation, cancellationToken).ConfigureAwait(false);
                if (result.IsFail)
                {
                    return ToModelRouterFailure(result);
                }

                var turn = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected model router failure."));

                return Results.Ok(MapChatCompletionResponse(invocation.ModelProfileName, turn));
            });


        endpoints.MapGet(
            "/agent/mcp/tools",
            static async Task<IResult> (
                IMcpToolCatalogClient mcpToolCatalogClient,
                CancellationToken cancellationToken) =>
            {
                var result = await mcpToolCatalogClient.ListToolsAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToMcpToolCatalogFailure(result);
                }

                var snapshot = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected MCP tool catalog failure."));

                return Results.Ok(MapMcpToolListResponse(snapshot));
            });



        endpoints.MapPost(
            "/agent/mcp/tools/call",
            static async Task<IResult> (
                ApiMcp.McpToolCallRequest? request,
                IMcpToolCallService toolCallService,
                CancellationToken cancellationToken) =>
            {
                var mappedRequest = request is null ? null : MapMcpToolCallRequest(request);
                var result = await toolCallService.CallToolAsync(mappedRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToMcpToolCallFailure(result);
                }

                var response = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected MCP tool call failure."));

                if (!response.Allowed || string.Equals(response.Status, Domain.Mcp.McpToolCallStatusNames.Denied, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(MapMcpToolCallResponse(response), statusCode: StatusCodes.Status403Forbidden);
                }

                return Results.Ok(MapMcpToolCallResponse(response));
            });


        endpoints.MapPost(
            "/agent/shell/exec",
            static async Task<IResult> (
                ApiShell.ShellExecutionRequest? request,
                IShellExecutionService shellExecutionService,
                CancellationToken cancellationToken) =>
            {
                var mappedRequest = request is null ? null : MapShellExecutionRequest(request);
                var result = await shellExecutionService.ExecuteAsync(mappedRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToShellExecutionFailure(result);
                }

                var response = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected shell execution failure."));

                if (!response.Allowed || string.Equals(response.Status, Domain.Shell.ShellExecutionStatusNames.Denied, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(MapShellExecutionResponse(response), statusCode: StatusCodes.Status403Forbidden);
                }

                var statusCode = string.Equals(response.Status, Domain.Shell.ShellExecutionStatusNames.Completed, StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status502BadGateway;

                return Results.Json(MapShellExecutionResponse(response), statusCode: statusCode);
            });

        endpoints.MapPost(
            "/agent/ssh/exec",
            static async Task<IResult> (
                ApiSsh.SshExecutionRequest? request,
                ISshExecutionService sshExecutionService,
                CancellationToken cancellationToken) =>
            {
                var mappedRequest = request is null ? null : MapSshExecutionRequest(request);
                var result = await sshExecutionService.ExecuteAsync(mappedRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToSshExecutionFailure(result);
                }

                var response = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected SSH execution failure."));

                if (!response.Allowed || string.Equals(response.Status, Domain.Ssh.SshExecutionStatusNames.Denied, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(MapSshExecutionResponse(response), statusCode: StatusCodes.Status403Forbidden);
                }

                var statusCode = string.Equals(response.Status, Domain.Ssh.SshExecutionStatusNames.Completed, StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status502BadGateway;

                return Results.Json(MapSshExecutionResponse(response), statusCode: statusCode);
            });


        endpoints.MapPost(
            "/agent/loops",
            static async Task<IResult> (
                ApiAgentLoops.AgentLoopRequest? request,
                IAutonomousLoopRunner loopRunner,
                CancellationToken cancellationToken) =>
            {
                var mappedRequest = request is null ? null : MapAgentLoopRequest(request);
                var result = await loopRunner.RunAsync(mappedRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToAgentLoopFailure(result);
                }

                var run = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected autonomous loop failure."));

                var statusCode = string.Equals(run.Status, AgentLoopStatusNames.Completed, StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK;

                return Results.Json(MapAgentLoopRun(run), statusCode: statusCode);
            });

        endpoints.MapPost(
            "/agent/runs",
            static async Task<IResult> (
                ApiAgentRuns.AgentRunRequest? request,
                IAgentRunService runService,
                CancellationToken cancellationToken) =>
            {
                var mappedRequest = request is null ? null : MapAgentRunRequest(request);
                var result = await runService.StartRunAsync(mappedRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToAgentRunFailure(result);
                }

                var run = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected agent run creation failure."));

                return Results.Created($"/agent/runs/{run.Id}", MapAgentRunResponse(run));
            });

        endpoints.MapGet(
            "/agent/runs/{id}",
            static async Task<IResult> (
                string id,
                IAgentRunService runService,
                CancellationToken cancellationToken) =>
            {
                var result = await runService.GetRunAsync(id, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    return ToAgentRunFailure(result);
                }

                var run = result.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected agent run read failure."));

                return Results.Ok(MapAgentRunResponse(run));
            });

        return endpoints;
    }

    public static IApplicationBuilder UseAgentRouterApiKeyGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var options = context.RequestServices
                .GetRequiredService<IOptionsMonitor<AgentRouterOptions>>()
                .CurrentValue;

            if (!options.RequireApiKey)
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(CreateError(
                        "AgentRouter:RequireApiKey is true, but AgentRouter:ApiKey is not configured.",
                        "server_configuration_error",
                        "api_key_missing"),
                    context.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            if (HasValidApiKey(context, options.ApiKey))
            {
                await next().ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(CreateError(
                    "Missing or invalid API key.",
                    "authentication_error",
                    "invalid_api_key"),
                context.RequestAborted)
                .ConfigureAwait(false);
        });
    }

    private static Fin<ModelInvocationRequest> MapChatCompletionRequest(
        OpenAiChatCompletionRequest? request,
        AgentRouterOptions options)
    {
        if (request is null)
        {
            return LanguageExt.Common.Error.New("Request body is required.");
        }

        if (request.Messages.Count == 0)
        {
            return LanguageExt.Common.Error.New("messages must contain at least one message.");
        }

        var messages = new List<ChatTurnMessage>();
        for (var index = 0; index < request.Messages.Count; index++)
        {
            var message = request.Messages[index];
            if (message is null)
            {
                return LanguageExt.Common.Error.New($"messages[{index}] cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(message.Role))
            {
                return LanguageExt.Common.Error.New($"messages[{index}].role is required.");
            }

            messages.Add(new ChatTurnMessage(message.Role, message.Content ?? string.Empty));
        }

        var profileName = string.IsNullOrWhiteSpace(request.Model)
            ? options.DefaultProfile
            : request.Model.Trim();

        return Fin<ModelInvocationRequest>.Succ(new ModelInvocationRequest(
            modelProfileName: profileName,
            messages: messages,
            temperature: request.Temperature,
            maxOutputTokens: request.MaxTokens));
    }

    private static OpenAiChatCompletionResponse MapChatCompletionResponse(
        string profileName,
        ModelTurnResult turn)
    {
        var response = new OpenAiChatCompletionResponse
        {
            Id = "chatcmpl-" + Guid.NewGuid().ToString("N"),
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = profileName,
            Usage = new OpenAiUsage
            {
                PromptTokens = turn.PromptTokens,
                CompletionTokens = turn.CompletionTokens,
                TotalTokens = turn.PromptTokens + turn.CompletionTokens
            }
        };

        response.Choices.Add(new OpenAiChatCompletionChoice
        {
            Index = 0,
            Message = new OpenAiChatMessage
            {
                Role = "assistant",
                Content = turn.Content
            },
            FinishReason = string.IsNullOrWhiteSpace(turn.FinishReason) ? "stop" : turn.FinishReason
        });

        return response;
    }


    private static ApiMcp.McpToolListResponse MapMcpToolListResponse(McpToolCatalogSnapshot snapshot)
    {
        var response = new ApiMcp.McpToolListResponse
        {
            Status = "ok",
            Transport = snapshot.Transport,
            ProtocolVersion = snapshot.ProtocolVersion,
            Server = new ApiMcp.McpServerDescriptor
            {
                Name = snapshot.ServerName,
                Version = snapshot.ServerVersion
            },
            ToolCount = snapshot.Tools.Count,
            ElapsedMilliseconds = snapshot.ElapsedMilliseconds
        };

        foreach (var tool in snapshot.Tools)
        {
            response.Tools.Add(new ApiMcp.McpToolDescriptor
            {
                Name = tool.Name,
                Title = tool.Title,
                Description = tool.Description,
                InputSchema = tool.InputSchema
            });
        }

        return response;
    }

    private static IResult ToMcpToolCallFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating MCP tool call error response."),
            Fail: error =>
            {
                var failure = ClassifyMcpToolCallError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToMcpToolCatalogFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating MCP tool catalog error response."),
            Fail: error =>
            {
                var failure = ClassifyMcpToolCatalogError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToRequestMappingFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating request mapping error response."),
            Fail: error =>
            {
                var failure = ClassifyRequestMappingError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToModelRouterFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating model router error response."),
            Fail: error =>
            {
                var failure = ClassifyModelRouterError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToAgentRunFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating agent run error response."),
            Fail: error =>
            {
                var failure = ClassifyAgentRunError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToAgentLoopFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating agent loop error response."),
            Fail: error =>
            {
                var failure = ClassifyAgentLoopError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToShellExecutionFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating shell execution error response."),
            Fail: error =>
            {
                var failure = ClassifyShellExecutionError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }

    private static IResult ToSshExecutionFailure<T>(Fin<T> result)
    {
        return result.Match<IResult>(
            Succ: _ => throw new InvalidOperationException("Expected failure when creating SSH execution error response."),
            Fail: error =>
            {
                var failure = ClassifySshExecutionError(error.Message);
                return Results.Json(failure.Response, statusCode: failure.StatusCode);
            });
    }


    private static RouterHttpFailure ClassifySshExecutionError(string message)
    {
        if (message.StartsWith("Request body", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "request_body_required");
        }

        if (message.StartsWith("profile is required", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "ssh_profile_required", "profile");
        }

        if (message.StartsWith("command is required", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "command_required", "command");
        }

        if (message.Contains("SSH secret missing", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "ssh_secret_missing", "profile");
        }

        if (message.Contains("SSH host key mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.Forbidden(message, "ssh_host_key_mismatch", "profile");
        }

        if (message.Contains("SSH authentication failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.Forbidden(message, "ssh_authentication_failed", "profile");
        }

        if (message.Contains("SSH connection failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("A connection attempt failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No route to host", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "ssh_connection_failed");
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "ssh_command_timeout");
        }

        if (message.Contains("disabled by configuration", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unknown SSH profile", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not allowed for SSH profile", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("explicitly denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("inline command switches", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Raw credentials are not accepted", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.Forbidden(message, "ssh_policy_denied", "command");
        }

        if (message.Contains("Private key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("missing Host", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("missing Username", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServerConfigurationError(message, "ssh_profile_configuration_error");
        }

        return RouterHttpFailure.UpstreamProviderError(message, "ssh_execution_error");
    }

    private static RouterHttpFailure ClassifyShellExecutionError(string message)
    {
        if (message.StartsWith("Request body", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "request_body_required");
        }

        if (message.StartsWith("command is required", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "command_required", "command");
        }

        if (message.Contains("disabled by configuration", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not in AgentRouter:ShellExecution:AllowedCommands", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("explicitly denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("escapes the configured shell execution root", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("inline command switches", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.Forbidden(message, "shell_command_not_allowed", "command");
        }

        if (message.Contains("working directory", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "working_directory_invalid", "working_directory");
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "shell_command_timeout");
        }

        return RouterHttpFailure.UpstreamProviderError(message, "shell_execution_error");
    }

    private static RouterHttpFailure ClassifyMcpToolCallError(string message)
    {
        if (message.StartsWith("Request body", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "request_body_required");
        }

        if (message.StartsWith("toolName is required", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "tool_name_required", "toolName");
        }

        if (message.Contains("not in AgentRouter:ToolExecution:AllowedTools", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("disabled by configuration", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.Forbidden(message, "tool_not_allowed", "toolName");
        }

        if (message.Contains("ExecutablePath", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("executable was not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("working directory", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("workspace root", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServerConfigurationError(message, "mcp_host_configuration_error");
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "mcp_host_timeout");
        }

        if (message.Contains("JSON-RPC error", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.UpstreamProviderError(message, "mcp_jsonrpc_error");
        }

        if (message.Contains("closed stdout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Failed to start", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "mcp_host_unavailable");
        }

        if (message.Contains("exceeded max output", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.UpstreamProviderError(message, "mcp_output_too_large");
        }

        return RouterHttpFailure.UpstreamProviderError(message, "mcp_tool_call_error");
    }

    private static RouterHttpFailure ClassifyMcpToolCatalogError(string message)
    {
        if (message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "mcp_client_disabled");
        }

        if (message.Contains("ExecutablePath", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("executable was not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("working directory", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("workspace root", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServerConfigurationError(message, "mcp_host_configuration_error");
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "mcp_host_timeout");
        }

        if (message.Contains("JSON-RPC error", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.UpstreamProviderError(message, "mcp_jsonrpc_error");
        }

        if (message.Contains("closed stdout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Failed to start", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "mcp_host_unavailable");
        }

        return RouterHttpFailure.UpstreamProviderError(message, "mcp_host_error");
    }

    private static RouterHttpFailure ClassifyRequestMappingError(string message)
    {
        if (message.StartsWith("messages must contain", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "messages_required", "messages");
        }

        if (message.StartsWith("messages[", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "invalid_message", "messages");
        }

        if (message.StartsWith("Request body", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "request_body_required");
        }

        return RouterHttpFailure.BadRequest(message, "invalid_request");
    }

    private static RouterHttpFailure ClassifyAgentRunError(string message)
    {
        if (message.StartsWith("Request body", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "request_body_required");
        }

        if (message.StartsWith("goal is required", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "goal_required", "goal");
        }

        if (message.StartsWith("run id", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "run_id_required", "id");
        }

        if (message.StartsWith("Agent run", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.NotFound(message, "run_not_found", "id");
        }

        return RouterHttpFailure.BadRequest(message, "invalid_agent_run_request");
    }

    private static RouterHttpFailure ClassifyAgentLoopError(string message)
    {
        if (message.StartsWith("Request body", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "request_body_required");
        }

        if (message.StartsWith("goal is required", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "goal_required", "goal");
        }

        if (message.StartsWith("max_steps", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "max_steps_invalid", "max_steps");
        }

        return RouterHttpFailure.BadRequest(message, "invalid_agent_loop_request");
    }

    private static Domain.AgentLoops.AgentLoopRequest MapAgentLoopRequest(ApiAgentLoops.AgentLoopRequest request)
    {
        return new Domain.AgentLoops.AgentLoopRequest
        {
            Goal = request.Goal,
            MaxSteps = request.MaxSteps,
            AllowedCapabilities = request.AllowedCapabilities,
            ToolName = request.ToolName,
            Arguments = request.Arguments?.Clone()
        };
    }

    private static Domain.AgentRuns.AgentRunRequest MapAgentRunRequest(ApiAgentRuns.AgentRunRequest request)
    {
        return new Domain.AgentRuns.AgentRunRequest
        {
            Model = request.Model,
            Goal = request.Goal,
            Instructions = request.Instructions,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens
        };
    }

    private static ApiAgentRuns.AgentRunResponse MapAgentRunResponse(Domain.AgentRuns.AgentRun run)
    {
        return new ApiAgentRuns.AgentRunResponse
        {
            Id = run.Id,
            Object = run.Object,
            Status = run.Status,
            Model = run.Model,
            Goal = run.Goal,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            CompletedAt = run.CompletedAt,
            Result = run.Result,
            Error = run.Error is null
                ? null
                : new ApiAgentRuns.AgentRunError
                {
                    Message = run.Error.Message,
                    Type = run.Error.Type,
                    Code = run.Error.Code
                },
            Artifacts = run.Artifacts.Select(MapAgentRunArtifact).ToList()
        };
    }

    private static ApiAgentRuns.AgentRunArtifact MapAgentRunArtifact(Domain.AgentRuns.AgentRunArtifact artifact)
    {
        return new ApiAgentRuns.AgentRunArtifact
        {
            Id = artifact.Id,
            Type = artifact.Type,
            Name = artifact.Name,
            Content = artifact.Content,
            CreatedAt = artifact.CreatedAt
        };
    }

    private static Domain.Mcp.McpToolCallRequest MapMcpToolCallRequest(ApiMcp.McpToolCallRequest request)
    {
        return new Domain.Mcp.McpToolCallRequest
        {
            ToolName = request.ToolName,
            Arguments = request.Arguments?.Clone(),
            TimeoutSeconds = request.TimeoutSeconds,
            MaxOutputChars = request.MaxOutputChars
        };
    }

    private static ApiMcp.McpToolCallResponse MapMcpToolCallResponse(Domain.Mcp.McpToolCallResponse response)
    {
        return new ApiMcp.McpToolCallResponse
        {
            Status = response.Status,
            ToolName = response.ToolName,
            Allowed = response.Allowed,
            PolicyDecision = response.PolicyDecision,
            PolicyReason = response.PolicyReason,
            TraceId = response.TraceId,
            Transport = response.Transport,
            ElapsedMilliseconds = response.ElapsedMilliseconds,
            Result = response.Result?.Clone(),
            ErrorMessage = response.ErrorMessage
        };
    }

    private static Domain.Shell.ShellExecutionRequest MapShellExecutionRequest(ApiShell.ShellExecutionRequest request)
    {
        return new Domain.Shell.ShellExecutionRequest
        {
            Command = request.Command,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            TimeoutSeconds = request.TimeoutSeconds
        };
    }

    private static ApiShell.ShellExecutionResponse MapShellExecutionResponse(Domain.Shell.ShellExecutionResponse response)
    {
        return new ApiShell.ShellExecutionResponse
        {
            Id = response.Id,
            Status = response.Status,
            Allowed = response.Allowed,
            PolicyDecision = response.PolicyDecision,
            PolicyReason = response.PolicyReason,
            Command = response.Command,
            Arguments = response.Arguments.ToList(),
            WorkingDirectory = response.WorkingDirectory,
            ExitCode = response.ExitCode,
            TimedOut = response.TimedOut,
            Stdout = response.Stdout,
            Stderr = response.Stderr,
            StdoutTruncated = response.StdoutTruncated,
            StderrTruncated = response.StderrTruncated,
            Summary = response.Summary,
            TraceId = response.TraceId,
            CreatedAt = response.CreatedAt,
            CompletedAt = response.CompletedAt,
            ElapsedMilliseconds = response.ElapsedMilliseconds
        };
    }

    private static Domain.Ssh.SshExecutionRequest MapSshExecutionRequest(ApiSsh.SshExecutionRequest request)
    {
        return new Domain.Ssh.SshExecutionRequest
        {
            Profile = request.Profile,
            Command = request.Command,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            TimeoutSeconds = request.TimeoutSeconds
        };
    }

    private static ApiSsh.SshExecutionResponse MapSshExecutionResponse(Domain.Ssh.SshExecutionResponse response)
    {
        return new ApiSsh.SshExecutionResponse
        {
            Id = response.Id,
            Status = response.Status,
            Allowed = response.Allowed,
            PolicyDecision = response.PolicyDecision,
            PolicyReason = response.PolicyReason,
            Profile = response.Profile,
            Host = response.Host,
            Port = response.Port,
            Username = response.Username,
            Command = response.Command,
            Arguments = response.Arguments.ToList(),
            WorkingDirectory = response.WorkingDirectory,
            ExitCode = response.ExitCode,
            TimedOut = response.TimedOut,
            Stdout = response.Stdout,
            Stderr = response.Stderr,
            StdoutTruncated = response.StdoutTruncated,
            StderrTruncated = response.StderrTruncated,
            Summary = response.Summary,
            TraceId = response.TraceId,
            CreatedAt = response.CreatedAt,
            CompletedAt = response.CompletedAt,
            ElapsedMilliseconds = response.ElapsedMilliseconds
        };
    }

    private static ApiAgentLoops.AgentLoopRun MapAgentLoopRun(Domain.AgentLoops.AgentLoopRun run)
    {
        return new ApiAgentLoops.AgentLoopRun
        {
            Id = run.Id,
            Status = run.Status,
            Goal = run.Goal,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            CompletedAt = run.CompletedAt,
            Result = run.Result,
            Error = run.Error is null
                ? null
                : new ApiAgentLoops.AgentLoopError
                {
                    Message = run.Error.Message,
                    Type = run.Error.Type,
                    Code = run.Error.Code
                },
            Steps = run.Steps.Select(MapAgentLoopStep).ToList()
        };
    }

    private static ApiAgentLoops.AgentLoopStep MapAgentLoopStep(Domain.AgentLoops.AgentLoopStep step)
    {
        return new ApiAgentLoops.AgentLoopStep
        {
            StepId = step.StepId,
            Sequence = step.Sequence,
            Phase = (ApiAgentLoops.AgentStepPhase)step.Phase,
            Capability = step.Capability,
            ToolName = step.ToolName,
            RiskLevel = (ApiAgentLoops.ToolRiskLevel)step.RiskLevel,
            Status = (ApiAgentLoops.ToolExecutionStatus)step.Status,
            Decision = (ApiAgentLoops.AgentDecisionType)step.Decision,
            PolicyDecision = step.PolicyDecision,
            StartedAt = step.StartedAt,
            CompletedAt = step.CompletedAt,
            InputSummary = step.InputSummary,
            OutputSummary = step.OutputSummary,
            ErrorMessage = step.ErrorMessage,
            TraceId = step.TraceId,
            ElapsedMilliseconds = step.ElapsedMilliseconds
        };
    }

    private static RouterHttpFailure ClassifyModelRouterError(string message)
    {
        if (message.StartsWith("Unknown model profile", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.BadRequest(message, "unknown_model", "model");
        }

        if (message.StartsWith("No chat model client is registered", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServerConfigurationError(message, "provider_client_missing");
        }

        if (message.Contains("BaseUrl", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServerConfigurationError(message, "provider_base_url_invalid");
        }

        if (message.StartsWith("Ollama request timed out", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "provider_timeout");
        }

        if (message.StartsWith("Ollama provider unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "provider_unavailable");
        }

        if (IsProviderConnectionFailure(message))
        {
            return RouterHttpFailure.ServiceUnavailable(message, "provider_unavailable");
        }

        if (message.StartsWith("Ollama returned HTTP", StringComparison.OrdinalIgnoreCase))
        {
            return RouterHttpFailure.UpstreamProviderError(message, "upstream_provider_error");
        }

        return RouterHttpFailure.UpstreamProviderError(message, "provider_error");
    }

    private static bool IsProviderConnectionFailure(string message)
    {
        return message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
               || message.Contains("A connection attempt failed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase);
    }

    private static OpenAiErrorResponse CreateError(
        string message,
        string type,
        string code,
        string? param = null)
    {
        return new OpenAiErrorResponse
        {
            Error = new OpenAiError
            {
                Message = message,
                Type = type,
                Param = param,
                Code = code
            }
        };
    }

    private sealed class RouterHttpFailure
    {
        private RouterHttpFailure(
            int statusCode,
            OpenAiErrorResponse response)
        {
            StatusCode = statusCode;
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public int StatusCode { get; }

        public OpenAiErrorResponse Response { get; }

        public static RouterHttpFailure BadRequest(
            string message,
            string code,
            string? param = null)
        {
            return new RouterHttpFailure(
                StatusCodes.Status400BadRequest,
                CreateError(message, "invalid_request_error", code, param));
        }

        public static RouterHttpFailure ServiceUnavailable(
            string message,
            string code)
        {
            return new RouterHttpFailure(
                StatusCodes.Status503ServiceUnavailable,
                CreateError(message, "service_unavailable_error", code));
        }

        public static RouterHttpFailure Forbidden(
            string message,
            string code,
            string? param = null)
        {
            return new RouterHttpFailure(
                StatusCodes.Status403Forbidden,
                CreateError(message, "invalid_request_error", code, param));
        }

        public static RouterHttpFailure NotFound(
            string message,
            string code,
            string? param = null)
        {
            return new RouterHttpFailure(
                StatusCodes.Status404NotFound,
                CreateError(message, "not_found_error", code, param));
        }

        public static RouterHttpFailure UpstreamProviderError(
            string message,
            string code)
        {
            return new RouterHttpFailure(
                StatusCodes.Status502BadGateway,
                CreateError(message, "upstream_provider_error", code));
        }

        public static RouterHttpFailure ServerConfigurationError(
            string message,
            string code)
        {
            return new RouterHttpFailure(
                StatusCodes.Status500InternalServerError,
                CreateError(message, "server_configuration_error", code));
        }
    }

    private static bool HasValidApiKey(HttpContext context, string expectedApiKey)
    {
        if (context.Request.Headers.TryGetValue("x-api-key", out var apiKeyValues) &&
            string.Equals(apiKeyValues.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var authValues))
        {
            return false;
        }

        var authorization = authValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        const string bearerPrefix = "Bearer ";
        return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(authorization[bearerPrefix.Length..].Trim(), expectedApiKey, StringComparison.Ordinal);
    }
}
