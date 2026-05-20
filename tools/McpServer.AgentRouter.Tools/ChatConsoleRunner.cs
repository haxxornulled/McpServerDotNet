using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class ChatConsoleSettings
{
    public Uri RouterBaseUrl { get; init; } = new("http://127.0.0.1:5177");

    public string Model { get; init; } = "fast-local";

    public string? SystemPrompt { get; init; }

    public string? SystemPromptFilePath { get; init; }

    public string? Prompt { get; init; }

    public string? PromptFilePath { get; init; }

    public double? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public int TimeoutSeconds { get; init; } = 120;

    public bool StreamRequested { get; init; } = true;

    public bool EnableToolCalling { get; init; } = true;

    public bool Interactive { get; init; }

    public string? TranscriptPath { get; init; }

    public string SessionName { get; init; } = "chat";

    public static ChatConsoleSettings FromOptions(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var model = options.GetString("chat-model", options.GetString("model", "fast-local"));
        var systemPrompt = options.GetString("system", string.Empty);
        var systemPromptFilePath = options.GetString("system-file", string.Empty);
        var prompt = options.GetString("prompt", string.Empty);
        var promptFilePath = options.GetString("prompt-file", string.Empty);

        return new ChatConsoleSettings
        {
            RouterBaseUrl = new Uri(options.GetString("router-base-url", "http://127.0.0.1:5177")),
            Model = string.IsNullOrWhiteSpace(model) ? "fast-local" : model,
            SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            SystemPromptFilePath = string.IsNullOrWhiteSpace(systemPromptFilePath) ? null : systemPromptFilePath,
            Prompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt,
            PromptFilePath = string.IsNullOrWhiteSpace(promptFilePath) ? null : promptFilePath,
            Temperature = options.GetNullableDouble("temperature"),
            MaxTokens = options.GetNullableInt("max-tokens"),
            TimeoutSeconds = options.GetInt("timeout-seconds", 120),
            StreamRequested = options.GetBool("stream", true),
            EnableToolCalling = options.GetBool("tools", true),
            Interactive = options.GetBool("interactive", false),
            TranscriptPath = options.GetString("transcript", string.Empty),
            SessionName = options.GetString("session-name", "chat")
        };
    }
}

internal sealed class ChatConsoleSessionResult
{
    public ChatConsoleSessionResult(
        int exitCode,
        string routerBaseUrl,
        string model,
        string? systemPrompt,
        string? transcriptPath,
        bool interactive,
        bool streamed,
        IReadOnlyList<ChatConsoleTurnResult> turns,
        string? errorMessage)
    {
        ExitCode = exitCode;
        RouterBaseUrl = routerBaseUrl;
        Model = model;
        SystemPrompt = systemPrompt;
        TranscriptPath = transcriptPath;
        Interactive = interactive;
        Streamed = streamed;
        Turns = turns;
        ErrorMessage = errorMessage;

        if (turns.Count > 0)
        {
            var last = turns[turns.Count - 1];
            Prompt = last.Prompt;
            Response = last.Response;
            FinishReason = last.FinishReason;
            PromptTokens = last.PromptTokens;
            CompletionTokens = last.CompletionTokens;
            TotalTokens = last.TotalTokens;
        }
    }

    public int ExitCode { get; }

    public string RouterBaseUrl { get; }

    public string Model { get; }

    public string? SystemPrompt { get; }

    public string? TranscriptPath { get; }

    public string? Prompt { get; }

    public string? Response { get; }

    public string? FinishReason { get; }

    public int PromptTokens { get; }

    public int CompletionTokens { get; }

    public int TotalTokens { get; }

    public bool Interactive { get; }

    public bool Streamed { get; }

    public string? ErrorMessage { get; }

    public IReadOnlyList<ChatConsoleTurnResult> Turns { get; }
}

internal sealed class ChatConsoleTurnResult
{
    public ChatConsoleTurnResult(
        string prompt,
        string response,
        string? finishReason,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        bool streamed,
        long elapsedMilliseconds)
    {
        Prompt = prompt;
        Response = response;
        FinishReason = finishReason;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        TotalTokens = totalTokens;
        Streamed = streamed;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public string Prompt { get; }

    public string Response { get; }

    public string? FinishReason { get; }

    public int PromptTokens { get; }

    public int CompletionTokens { get; }

    public int TotalTokens { get; }

    public bool Streamed { get; }

    public long ElapsedMilliseconds { get; }
}

internal sealed class ChatConsoleModelReply
{
    public ChatConsoleModelReply(
        string content,
        string finishReason,
        int promptTokens,
        int completionTokens,
        long elapsedMilliseconds,
        IReadOnlyList<ChatConsoleToolCall>? toolCalls)
    {
        Content = content;
        FinishReason = string.IsNullOrWhiteSpace(finishReason) ? "stop" : finishReason;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        ElapsedMilliseconds = elapsedMilliseconds;
        ToolCalls = toolCalls;
    }

    public string Content { get; }

    public string FinishReason { get; }

    public int PromptTokens { get; }

    public int CompletionTokens { get; }

    public long ElapsedMilliseconds { get; }

    public IReadOnlyList<ChatConsoleToolCall>? ToolCalls { get; }
}

internal sealed class ChatConsoleToolCall
{
    public ChatConsoleToolCall(string id, string name, JsonNode? arguments)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Tool call id is required.", nameof(id)) : id;
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Tool name is required.", nameof(name)) : name;
        Arguments = arguments;
    }

    public string Id { get; }

    public string Name { get; }

    public JsonNode? Arguments { get; }
}

internal sealed class ChatConsoleTranscript
{
    public string SessionName { get; init; } = string.Empty;

    public string RouterBaseUrl { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string? SystemPrompt { get; init; }

    public bool Interactive { get; init; }

    public bool Streamed { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset FinishedAtUtc { get; init; }

    public IReadOnlyList<ChatConsoleTranscriptTurn> Turns { get; init; } = Array.Empty<ChatConsoleTranscriptTurn>();
}

internal sealed class ChatConsoleTranscriptTurn
{
    public string Prompt { get; init; } = string.Empty;

    public string Response { get; init; } = string.Empty;

    public string? FinishReason { get; init; }

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int TotalTokens { get; init; }

    public bool Streamed { get; init; }

    public long ElapsedMilliseconds { get; init; }
}

internal sealed class ChatConsoleRunner
{
    private const int MinFrameWidth = 78;
    private const int MaxFrameWidth = 120;

    private readonly ChatConsoleSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly bool _inputRedirected;
    private readonly ChatConsoleInputController _inputController;
    private readonly IChatConsoleStatusIndicatorFactory _statusIndicatorFactory;
    private readonly WebSearchConsoleToolClient _webSearchToolClient;
    private IReadOnlyList<ChatConsoleToolDefinition>? _toolDefinitions;
    private readonly JsonSerializerOptions _jsonOptions = JsonOptions.CreateIndented();

    public ChatConsoleRunner(
        ChatConsoleSettings settings,
        HttpClient httpClient,
        TextReader input,
        TextWriter output,
        TextWriter error,
        bool inputRedirected,
        IClipboardTextProvider? clipboardTextProvider = null,
        IChatConsoleStatusIndicatorFactory? statusIndicatorFactory = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _inputRedirected = inputRedirected;
        _inputController = new ChatConsoleInputController(input, clipboardTextProvider ?? new PowerShellClipboardTextProvider());
        _statusIndicatorFactory = statusIndicatorFactory ?? new TerminalChatConsoleStatusIndicatorFactory(output);
        _webSearchToolClient = new WebSearchConsoleToolClient(httpClient, settings.RouterBaseUrl);
    }

    public async Task<ChatConsoleSessionResult> RunAsync(CancellationToken cancellationToken)
    {
        if (CliOutput.IsJson && ShouldRunInteractive())
        {
            return CreateFailureResult("JSON output requires --prompt or redirected stdin for chat.");
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var streamed = _settings.StreamRequested && !CliOutput.IsJson;
        var turns = new List<ChatConsoleTurnResult>();
        var conversation = CreateConversation();

        WriteSessionHeader(streamed, ShouldRunInteractive());

        if (ShouldRunInteractive())
        {
            await RunInteractiveAsync(conversation, turns, streamed, cancellationToken).ConfigureAwait(false);
            var interactiveResult = new ChatConsoleSessionResult(
                exitCode: 0,
                routerBaseUrl: _settings.RouterBaseUrl.ToString(),
                model: _settings.Model,
                systemPrompt: _settings.SystemPrompt,
                transcriptPath: _settings.TranscriptPath,
                interactive: true,
                streamed: streamed,
                turns: turns,
                errorMessage: null);
            await WriteTranscriptAsync(interactiveResult, startedAtUtc, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return interactiveResult;
        }

        var prompt = await ResolvePromptAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return CreateFailureResult("Prompt is required. Use --prompt, pipe text into stdin, or run interactively.");
        }

        var turn = await ExecuteTurnAsync(conversation, prompt, streamed, cancellationToken).ConfigureAwait(false);
        if (turn is null)
        {
            return CreateFailureResult("Chat completion failed.");
        }

        turns.Add(turn);
        var singleTurnResult = new ChatConsoleSessionResult(
            exitCode: 0,
            routerBaseUrl: _settings.RouterBaseUrl.ToString(),
            model: _settings.Model,
            systemPrompt: _settings.SystemPrompt,
            transcriptPath: _settings.TranscriptPath,
            interactive: false,
            streamed: streamed,
            turns: turns,
            errorMessage: null);
        await WriteTranscriptAsync(singleTurnResult, startedAtUtc, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        return singleTurnResult;
    }

    private async Task RunInteractiveAsync(
        List<ChatMessage> conversation,
        List<ChatConsoleTurnResult> turns,
        bool streamed,
        CancellationToken cancellationToken)
    {
        var seedPrompt = _settings.Prompt;
        if (!string.IsNullOrWhiteSpace(seedPrompt))
        {
            await RunOneTurnAsync(conversation, turns, seedPrompt, streamed, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _output.Write("You> ");
            _output.Flush();

            var submission = await _inputController.ReadNextSubmissionAsync(cancellationToken).ConfigureAwait(false);
            if (submission.Kind == ChatConsoleInputKind.EndOfStream)
            {
                break;
            }

            if (submission.Kind == ChatConsoleInputKind.Exit)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(submission.Notice) && !CliOutput.IsJson)
            {
                _output.WriteLine(submission.Notice);
            }

            if (submission.Kind == ChatConsoleInputKind.ToolCommand)
            {
                await RunToolCommandAsync(conversation, submission, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrWhiteSpace(submission.Prompt))
            {
                continue;
            }

            await RunOneTurnAsync(conversation, turns, submission.Prompt, streamed, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunOneTurnAsync(
        List<ChatMessage> conversation,
        List<ChatConsoleTurnResult> turns,
        string prompt,
        bool streamed,
        CancellationToken cancellationToken)
    {
        var turn = await ExecuteTurnAsync(conversation, prompt, streamed, cancellationToken).ConfigureAwait(false);
        if (turn is null)
        {
            return;
        }

        turns.Add(turn);

        if (!CliOutput.IsJson)
        {
            WriteTurnSummary(turn);
        }
    }

    private async Task RunToolCommandAsync(
        List<ChatMessage> conversation,
        ChatConsoleInputSubmission submission,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(submission.ToolName, "web.search", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(submission.Prompt))
        {
            return;
        }

        try
        {
            await using var status = CreateStatusIndicator();
            var searchResult = await _webSearchToolClient
                .SearchAsync(submission.Prompt, maxResults: 5, cancellationToken)
                .ConfigureAwait(false);

            var summaryMarkdown = BuildWebSearchMarkdown(searchResult);
            if (!CliOutput.IsJson)
            {
                WriteMarkdownBlock("Web Search", summaryMarkdown);
                _output.WriteLine("Search results added to context for the next turn.");
                _output.WriteLine();
            }

            conversation.Add(new ChatMessage("system", BuildSearchContext(searchResult)));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            if (!CliOutput.IsJson)
            {
                _error.WriteLine(ex.Message);
            }
        }
    }

    private async Task<ChatConsoleTurnResult?> ExecuteTurnAsync(
        List<ChatMessage> conversation,
        string prompt,
        bool streamed,
        CancellationToken cancellationToken)
    {
        var promptMessage = new ChatMessage("user", prompt);
        conversation.Add(promptMessage);

        if (!CliOutput.IsJson)
        {
            WritePromptPanel(prompt);
        }

        try
        {
            await using var status = CreateStatusIndicator();

            if (_settings.EnableToolCalling)
            {
                var result = await ExecuteToolAwareTurnAsync(conversation, prompt, streamed, status, cancellationToken).ConfigureAwait(false);
                if (result is null)
                {
                    conversation.RemoveAt(conversation.Count - 1);
                    return null;
                }

                conversation.Add(new ChatMessage("assistant", result.Response));
                return result;
            }

            if (streamed)
            {
                var streamingResult = await ExecuteStreamingTurnAsync(conversation, prompt, null, status, cancellationToken).ConfigureAwait(false);
                if (streamingResult is null)
                {
                    conversation.RemoveAt(conversation.Count - 1);
                    return null;
                }

                conversation.Add(new ChatMessage("assistant", streamingResult.Response));
                return streamingResult;
            }

            var nonStreamingReply = await ExecuteNonStreamingTurnAsync(conversation, null, status, cancellationToken).ConfigureAwait(false);
            if (nonStreamingReply is null)
            {
                conversation.RemoveAt(conversation.Count - 1);
                return null;
            }

            if (!CliOutput.IsJson)
            {
                WriteAssistantPanel(nonStreamingReply.Content);
            }

            conversation.Add(new ChatMessage("assistant", nonStreamingReply.Content));
            return new ChatConsoleTurnResult(
                prompt,
                nonStreamingReply.Content,
                nonStreamingReply.FinishReason,
                nonStreamingReply.PromptTokens,
                nonStreamingReply.CompletionTokens,
                nonStreamingReply.PromptTokens + nonStreamingReply.CompletionTokens,
                streamed: false,
                nonStreamingReply.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            conversation.RemoveAt(conversation.Count - 1);
            if (!CliOutput.IsJson)
            {
                _error.WriteLine(ex.Message);
            }

            return null;
        }
    }

    private async Task<ChatConsoleTurnResult?> ExecuteToolAwareTurnAsync(
        List<ChatMessage> conversation,
        string prompt,
        bool streamed,
        IChatConsoleStatusIndicator? status,
        CancellationToken cancellationToken)
    {
        var toolDefinitions = await GetToolDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var reply = await ExecuteNonStreamingTurnAsync(conversation, toolDefinitions, status, cancellationToken).ConfigureAwait(false);
        if (reply is null)
        {
            return null;
        }

        var toolCalls = reply.ToolCalls ?? TryParseAssistantToolCalls(reply.Content);
        if (toolCalls is null || toolCalls.Count == 0)
        {
            if (!CliOutput.IsJson)
            {
                WriteAssistantPanel(reply.Content);
            }

            return new ChatConsoleTurnResult(
                prompt,
                reply.Content,
                reply.FinishReason,
                reply.PromptTokens,
                reply.CompletionTokens,
                reply.PromptTokens + reply.CompletionTokens,
                streamed: false,
                reply.ElapsedMilliseconds);
        }

        AppendAssistantToolCallMessage(conversation, string.Empty, toolCalls);

        if (!CliOutput.IsJson)
        {
            WriteToolCallPanel(toolCalls);
        }

        foreach (var toolCall in toolCalls)
        {
            var toolResult = await ExecuteToolCallAsync(toolCall, cancellationToken).ConfigureAwait(false);
            if (!CliOutput.IsJson)
            {
                WriteToolResultPanel(toolCall.Name, toolResult);
            }

            conversation.Add(new ChatMessage("tool", toolResult, toolCall.Id));
        }

        if (streamed)
        {
            return await ExecuteStreamingTurnAsync(conversation, prompt, null, status, cancellationToken).ConfigureAwait(false);
        }

        var finalReply = await ExecuteNonStreamingTurnAsync(conversation, null, status, cancellationToken).ConfigureAwait(false);
        if (finalReply is null)
        {
            return null;
        }

        if (!CliOutput.IsJson)
        {
            WriteAssistantPanel(finalReply.Content);
        }

        return new ChatConsoleTurnResult(
            prompt,
            finalReply.Content,
            finalReply.FinishReason,
            finalReply.PromptTokens,
            finalReply.CompletionTokens,
            finalReply.PromptTokens + finalReply.CompletionTokens,
            streamed: false,
            finalReply.ElapsedMilliseconds);
    }

    private async Task<ChatConsoleModelReply?> ExecuteNonStreamingTurnAsync(
        IReadOnlyList<ChatMessage> conversation,
        IReadOnlyList<ChatConsoleToolDefinition>? toolDefinitions,
        IChatConsoleStatusIndicator? status,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(conversation, stream: false, toolDefinitions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            if (!CliOutput.IsJson)
            {
                _error.WriteLine(ExtractErrorMessage(body, $"HTTP {(int)response.StatusCode}"));
            }

            return null;
        }

        JsonNode? json = null;
        try
        {
            json = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            if (!CliOutput.IsJson)
            {
                _error.WriteLine($"Invalid JSON response: {ex.Message}");
            }

            return null;
        }

        var assistant = JsonFieldReader.GetString(json, "choices", 0, "message", "content") ?? string.Empty;
        var finishReason = JsonFieldReader.GetString(json, "choices", 0, "finish_reason") ?? "stop";
        var promptTokens = JsonFieldReader.GetInt32(json, "usage", "prompt_tokens") ?? 0;
        var completionTokens = JsonFieldReader.GetInt32(json, "usage", "completion_tokens") ?? 0;
        var toolCalls = ReadToolCalls(json);

        status?.Stop();
        return new ChatConsoleModelReply(
            assistant,
            finishReason,
            promptTokens,
            completionTokens,
            stopwatch.ElapsedMilliseconds,
            toolCalls);
    }

    private async Task<ChatConsoleTurnResult?> ExecuteStreamingTurnAsync(
        IReadOnlyList<ChatMessage> conversation,
        string prompt,
        IReadOnlyList<ChatConsoleToolDefinition>? toolDefinitions,
        IChatConsoleStatusIndicator? status,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(conversation, stream: true, toolDefinitions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!CliOutput.IsJson)
            {
                _error.WriteLine(ExtractErrorMessage(body, $"HTTP {(int)response.StatusCode}"));
            }

            return null;
        }

        var assistant = new StringBuilder();
        var finishReason = "stop";
        var currentEvent = string.Empty;
        var livePanel = !CliOutput.IsJson && CanLiveRedraw()
            ? new LiveMarkdownPanel(_output, "Assistant", GetFrameWidth)
            : null;

        await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                currentEvent = string.Empty;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line[6..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[5..].TrimStart();
            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            JsonNode? json;
            try
            {
                json = JsonNode.Parse(payload);
            }
            catch (JsonException ex)
            {
                stopwatch.Stop();
                if (!CliOutput.IsJson)
                {
                    _error.WriteLine($"Invalid streaming JSON chunk: {ex.Message}");
                }

                return null;
            }

            var errorMessage = JsonFieldReader.GetString(json, "error", "message");
            if (!string.IsNullOrWhiteSpace(errorMessage) || string.Equals(currentEvent, "error", StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                if (!CliOutput.IsJson)
                {
                    _error.WriteLine(errorMessage ?? "Chat streaming failed.");
                }

                return null;
            }

            var content = JsonFieldReader.GetString(json, "choices", 0, "delta", "content") ?? string.Empty;
            if (content.Length > 0)
            {
                status?.Stop();
                assistant.Append(content);
                if (livePanel is not null)
                {
                    livePanel.Render(assistant.ToString());
                }
            }

            var chunkFinishReason = JsonFieldReader.GetString(json, "choices", 0, "finish_reason");
            if (!string.IsNullOrWhiteSpace(chunkFinishReason))
            {
                finishReason = chunkFinishReason;
            }
        }

        stopwatch.Stop();

        if (livePanel is not null)
        {
            livePanel.Finish(assistant.ToString());
        }
        else if (!CliOutput.IsJson)
        {
            WriteAssistantPanel(assistant.ToString());
        }

        return new ChatConsoleTurnResult(
            prompt,
            assistant.ToString(),
            finishReason,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0,
            streamed: true,
            stopwatch.ElapsedMilliseconds);
    }

    private IChatConsoleStatusIndicator? CreateStatusIndicator()
    {
        if (CliOutput.IsJson)
        {
            return null;
        }

        return _statusIndicatorFactory.Create("Waiting on GPU...");
    }

    private HttpRequestMessage BuildRequest(
        IReadOnlyList<ChatMessage> conversation,
        bool stream,
        IReadOnlyList<ChatConsoleToolDefinition>? toolDefinitions)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_settings.RouterBaseUrl, "/v1/chat/completions"));

        var messages = new object[conversation.Count];
        for (var index = 0; index < conversation.Count; index++)
        {
            var message = conversation[index];
            messages[index] = new
            {
                role = message.Role,
                content = message.Content,
                tool_call_id = message.ToolCallId,
                tool_calls = message.ToolCalls is null
                    ? null
                    : message.ToolCalls.Select(static toolCall => new
                    {
                        id = toolCall.Id,
                        type = "function",
                        function = new
                        {
                            name = toolCall.Name,
                            arguments = toolCall.Arguments?.ToJsonString() ?? "{}"
                        }
                    }).ToArray()
            };
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = _settings.Model,
            ["messages"] = messages,
            ["stream"] = stream
        };

        if (toolDefinitions is not null)
        {
            payload["tools"] = toolDefinitions.Select(static tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.InputSchema
                }
            }).ToArray();
        }

        if (_settings.Temperature.HasValue)
        {
            payload["temperature"] = _settings.Temperature.Value;
        }

        if (_settings.MaxTokens.HasValue)
        {
            payload["max_tokens"] = _settings.MaxTokens.Value;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions.CreateCompact());
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<IReadOnlyList<ChatConsoleToolDefinition>?> GetToolDefinitionsAsync(CancellationToken cancellationToken)
    {
        if (!_settings.EnableToolCalling)
        {
            return null;
        }

        if (_toolDefinitions is not null)
        {
            return _toolDefinitions;
        }

        try
        {
            var response = await HttpJson.GetAsync(_httpClient, new Uri(_settings.RouterBaseUrl, "/agent/mcp/tools"), cancellationToken).ConfigureAwait(false);
            if (response.Success && response.Json is not null)
            {
                _toolDefinitions = ParseToolDefinitions(response.Json) ?? BuildFallbackToolDefinitions();
                return _toolDefinitions;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            if (!CliOutput.IsJson)
            {
                _error.WriteLine($"Tool catalog lookup failed, falling back to built-in tools: {ex.Message}");
            }
        }

        _toolDefinitions = BuildFallbackToolDefinitions();
        return _toolDefinitions;
    }

    private ChatConsoleSessionResult CreateFailureResult(string errorMessage)
    {
        if (!CliOutput.IsJson)
        {
            _error.WriteLine(errorMessage);
        }

        return new ChatConsoleSessionResult(
            exitCode: 2,
            routerBaseUrl: _settings.RouterBaseUrl.ToString(),
            model: _settings.Model,
            systemPrompt: _settings.SystemPrompt,
            transcriptPath: _settings.TranscriptPath,
            interactive: false,
            streamed: false,
            turns: Array.Empty<ChatConsoleTurnResult>(),
            errorMessage: errorMessage);
    }

    private bool ShouldRunInteractive()
    {
        if (_settings.Interactive)
        {
            return !CliOutput.IsJson;
        }

        return string.IsNullOrWhiteSpace(_settings.Prompt)
            && string.IsNullOrWhiteSpace(_settings.PromptFilePath)
            && !_inputRedirected
            && !CliOutput.IsJson;
    }

    private async Task<string?> ResolvePromptAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_settings.Prompt))
        {
            return _settings.Prompt;
        }

        if (!string.IsNullOrWhiteSpace(_settings.PromptFilePath))
        {
            var promptFilePath = Path.GetFullPath(_settings.PromptFilePath);
            return await File.ReadAllTextAsync(promptFilePath, cancellationToken).ConfigureAwait(false);
        }

        if (!_inputRedirected)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var prompt = await _input.ReadToEndAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }

    private void WriteSessionHeader(bool streamed, bool interactive)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        var lines = new List<string>(8)
        {
            FormattableString.Invariant($"Session     : {_settings.SessionName}"),
            FormattableString.Invariant($"Mode        : {(interactive ? "interactive" : "single-turn")}"),
            FormattableString.Invariant($"Model       : {_settings.Model}"),
            FormattableString.Invariant($"Router      : {_settings.RouterBaseUrl}"),
            FormattableString.Invariant($"Streaming   : {(streamed ? "on" : "off")}")
        };

        if (!string.IsNullOrWhiteSpace(_settings.SystemPrompt))
        {
            lines.Add("System      : set");
        }

        if (!string.IsNullOrWhiteSpace(_settings.SystemPromptFilePath))
        {
            lines.Add(FormattableString.Invariant($"System file : {_settings.SystemPromptFilePath}"));
        }

        if (!string.IsNullOrWhiteSpace(_settings.TranscriptPath))
        {
            lines.Add(FormattableString.Invariant($"Transcript  : {_settings.TranscriptPath}"));
        }

        WriteFramedBlock("Chat Console", lines, GetFrameWidth());
        _output.WriteLine("Type 'exit' or 'quit' to leave. Use /paste to pull from the clipboard; if that is unavailable, paste text and finish with /end.");
        _output.WriteLine();
    }

    private void WritePromptPanel(string prompt)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        WriteMarkdownBlock("You", prompt);
    }

    private void WriteAssistantPanel(string response)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        WriteMarkdownBlock("Assistant", response);
    }

    private static string BuildSearchContext(WebSearchConsoleResult searchResult)
    {
        var builder = new StringBuilder();
        builder.Append("Web search results for: ");
        builder.AppendLine(searchResult.Query);

        if (!string.IsNullOrWhiteSpace(searchResult.RawText))
        {
            builder.AppendLine(searchResult.RawText);
            return builder.ToString();
        }

        for (var index = 0; index < searchResult.Results.Count; index++)
        {
            var item = searchResult.Results[index];
            builder.Append(index + 1);
            builder.Append(". ");
            builder.AppendLine(item.Title);
            builder.Append("   ");
            builder.AppendLine(item.Url);
            if (!string.IsNullOrWhiteSpace(item.Snippet))
            {
                builder.Append("   ");
                builder.AppendLine(item.Snippet);
            }
        }

        return builder.ToString();
    }

    private static string BuildWebSearchMarkdown(WebSearchConsoleResult searchResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine(FormattableString.Invariant($"Search results for: {searchResult.Query}"));
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(searchResult.RawText))
        {
            builder.AppendLine(searchResult.RawText);
            return builder.ToString();
        }

        if (searchResult.Results.Count == 0)
        {
            builder.AppendLine("No results returned.");
            return builder.ToString();
        }

        for (var index = 0; index < searchResult.Results.Count; index++)
        {
            var item = searchResult.Results[index];
            builder.Append(index + 1);
            builder.Append(". [");
            builder.Append(item.Title);
            builder.Append("](");
            builder.Append(item.Url);
            builder.AppendLine(")");

            if (!string.IsNullOrWhiteSpace(item.Snippet))
            {
                builder.Append("   ");
                builder.AppendLine(item.Snippet);
            }
        }

        return builder.ToString();
    }

    private void AppendAssistantToolCallMessage(
        List<ChatMessage> conversation,
        string assistantContent,
        IReadOnlyList<ChatConsoleToolCall> toolCalls)
    {
        if (toolCalls.Count > 0)
        {
            conversation.Add(new ChatMessage("assistant", assistantContent, toolCalls: toolCalls));
        }
    }

    private async Task<string> ExecuteToolCallAsync(
        ChatConsoleToolCall toolCall,
        CancellationToken cancellationToken)
    {
        if (string.Equals(toolCall.Name, "web.search", StringComparison.OrdinalIgnoreCase))
        {
            var query = toolCall.Arguments?["query"]?.GetValue<string>() ?? string.Empty;
            var maxResults = toolCall.Arguments?["maxResults"]?.GetValue<int>() ?? 5;
            var searchResult = await _webSearchToolClient.SearchAsync(query, maxResults, cancellationToken).ConfigureAwait(false);
            return BuildSearchContext(searchResult);
        }

        var response = await HttpJson.PostAsync(
                _httpClient,
                new Uri(_settings.RouterBaseUrl, "/agent/mcp/tools/call"),
                new
                {
                    toolName = toolCall.Name,
                    arguments = toolCall.Arguments ?? new JsonObject(),
                    timeoutSeconds = _settings.TimeoutSeconds,
                    maxOutputChars = 20_000
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage);
        }

        return BuildGenericToolContext(toolCall.Name, response.Json);
    }

    private static string BuildGenericToolContext(string toolName, JsonNode? responseJson)
    {
        if (responseJson is null)
        {
            return $"Tool '{toolName}' returned no response.";
        }

        var resultNode = responseJson["result"];
        if (resultNode is null)
        {
            return $"Tool '{toolName}' returned no result payload.";
        }

        var structured = resultNode["structuredContent"];
        if (string.Equals(toolName, "activity.route", StringComparison.OrdinalIgnoreCase))
        {
            var activityRouteContext = BuildActivityRouteContext(structured, resultNode);
            if (!string.IsNullOrWhiteSpace(activityRouteContext))
            {
                return activityRouteContext;
            }
        }

        if (structured is not null)
        {
            return structured.ToJsonString(JsonOptions.CreateIndented());
        }

        var contentText = ReadTextContent(resultNode);
        if (!string.IsNullOrWhiteSpace(contentText))
        {
            return contentText;
        }

        return resultNode.ToJsonString(JsonOptions.CreateIndented());
    }

    private static string BuildActivityRouteContext(JsonNode? structured, JsonNode resultNode)
    {
        var routeNode = structured as JsonObject ?? resultNode["structuredContent"] as JsonObject;
        if (routeNode is null)
        {
            return string.Empty;
        }

        var activity = JsonFieldReader.GetString(routeNode, "activity") ?? "unknown";
        var confidence = TryReadDouble(routeNode["confidence"], out var confidenceValue)
            ? confidenceValue
            : 0d;
        var reason = JsonFieldReader.GetString(routeNode, "reason");
        var requiresWorkspace = JsonFieldReader.GetBoolean(routeNode, "requiresWorkspace") ?? false;
        var requiresShell = JsonFieldReader.GetBoolean(routeNode, "requiresShell") ?? false;
        var requiresStructuredOutput = JsonFieldReader.GetBoolean(routeNode, "requiresStructuredOutput") ?? false;
        var schemaName = JsonFieldReader.GetString(routeNode, "schemaName");

        var builder = new StringBuilder();
        builder.Append("Activity route: ");
        builder.AppendLine(activity);
        builder.Append("Confidence: ");
        builder.AppendLine(confidence.ToString("0.###", CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            builder.Append("Schema: ");
            builder.AppendLine(schemaName);
        }

        builder.Append("Needs workspace: ");
        builder.AppendLine(requiresWorkspace ? "yes" : "no");
        builder.Append("Needs shell: ");
        builder.AppendLine(requiresShell ? "yes" : "no");
        builder.Append("Needs structured output: ");
        builder.AppendLine(requiresStructuredOutput ? "yes" : "no");

        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Append("Reason: ");
            builder.AppendLine(reason);
        }

        return builder.ToString().TrimEnd();
    }

    private static bool TryReadDouble(JsonNode? node, out double value)
    {
        if (node is null)
        {
            value = default;
            return false;
        }

        if (node.GetValueKind() == JsonValueKind.Number && double.TryParse(node.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(node.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private static string? ReadTextContent(JsonNode resultNode)
    {
        if (resultNode["content"] is not JsonArray contentArray || contentArray.Count == 0)
        {
            return null;
        }

        foreach (var item in contentArray)
        {
            if (item is not JsonObject contentObject)
            {
                continue;
            }

            var text = contentObject["text"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private void WriteToolCallPanel(IReadOnlyList<ChatConsoleToolCall> toolCalls)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        var lines = new List<string>(toolCalls.Count + 1)
        {
            "Model requested tool use:"
        };

        for (var index = 0; index < toolCalls.Count; index++)
        {
            var toolCall = toolCalls[index];
            lines.Add(FormattableString.Invariant($"{index + 1}. {toolCall.Name}"));
        }

        WriteFramedBlock("Tool Request", lines, GetFrameWidth());
    }

    private void WriteToolResultPanel(string toolName, string content)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        var title = string.Equals(toolName, "web.search", StringComparison.OrdinalIgnoreCase)
            ? "Web Search"
            : string.Equals(toolName, "activity.route", StringComparison.OrdinalIgnoreCase)
                ? "Activity Route"
                : "Tool Result";

        WriteMarkdownBlock(title, content);
    }

    private static IReadOnlyList<ChatConsoleToolCall>? ReadToolCalls(JsonNode? json)
    {
        if (json is null)
        {
            return null;
        }

        var messageNode = json["choices"]?[0]?["message"];
        if (messageNode is null || messageNode["tool_calls"] is not JsonArray toolCallsArray)
        {
            return null;
        }

        var toolCalls = new List<ChatConsoleToolCall>();
        foreach (var item in toolCallsArray)
        {
            if (item is not JsonObject toolCallObject)
            {
                continue;
            }

            var id = toolCallObject["id"]?.GetValue<string>() ?? string.Empty;
            var function = toolCallObject["function"] as JsonObject;
            var name = function?["name"]?.GetValue<string>() ?? string.Empty;
            var argumentsText = function?["arguments"]?.GetValue<string>() ?? "{}";

            JsonNode? arguments;
            try
            {
                arguments = JsonNode.Parse(argumentsText);
            }
            catch (JsonException)
            {
                arguments = JsonNode.Parse("{}");
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            toolCalls.Add(new ChatConsoleToolCall(id, name, arguments));
        }

        return toolCalls.Count == 0 ? null : toolCalls;
    }

    private static IReadOnlyList<ChatConsoleToolCall>? TryParseAssistantToolCalls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is not JsonObject toolCallObject)
        {
            return null;
        }

        var name = toolCallObject["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var id = toolCallObject["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "assistant-content-1";
        }

        JsonNode? arguments = toolCallObject["arguments"];
        if (arguments is null && toolCallObject["tool_calls"] is JsonArray toolCallsArray && toolCallsArray.Count > 0)
        {
            return ReadToolCallsFromArray(toolCallsArray);
        }

        if (arguments is JsonValue argumentsValue && argumentsValue.TryGetValue<string>(out var argumentsText))
        {
            try
            {
                arguments = JsonNode.Parse(argumentsText);
            }
            catch (JsonException)
            {
                arguments = JsonNode.Parse("{}");
            }
        }

        return [new ChatConsoleToolCall(id, name, arguments ?? JsonNode.Parse("{}"))];
    }

    private static IReadOnlyList<ChatConsoleToolCall>? ReadToolCallsFromArray(JsonArray toolCallsArray)
    {
        var toolCalls = new List<ChatConsoleToolCall>();
        foreach (var item in toolCallsArray)
        {
            if (item is not JsonObject toolCallObject)
            {
                continue;
            }

            var id = toolCallObject["id"]?.GetValue<string>() ?? string.Empty;
            var function = toolCallObject["function"] as JsonObject;
            var name = function?["name"]?.GetValue<string>() ?? string.Empty;
            var argumentsText = function?["arguments"]?.GetValue<string>() ?? "{}";

            JsonNode? arguments;
            try
            {
                arguments = JsonNode.Parse(argumentsText);
            }
            catch (JsonException)
            {
                arguments = JsonNode.Parse("{}");
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            toolCalls.Add(new ChatConsoleToolCall(id, name, arguments));
        }

        return toolCalls.Count == 0 ? null : toolCalls;
    }

    private static IReadOnlyList<ChatConsoleToolDefinition> BuildFallbackToolDefinitions()
    {
        return
        [
            new ChatConsoleToolDefinition(
                "web.search",
                "Searches the web for a query and returns ranked results.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new
                    {
                        query = new { type = "string" },
                        maxResults = new { type = "integer", @default = 5, minimum = 1, maximum = 20 }
                    },
                    required = new[] { "query" }
                }))
        ];
    }

    private static IReadOnlyList<ChatConsoleToolDefinition>? ParseToolDefinitions(JsonNode json)
    {
        if (json["tools"] is not JsonArray toolsArray || toolsArray.Count == 0)
        {
            return null;
        }

        var mapped = new List<ChatConsoleToolDefinition>(toolsArray.Count);
        foreach (var item in toolsArray)
        {
            if (item is not JsonObject toolObject)
            {
                continue;
            }

            var name = toolObject["name"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = toolObject["description"]?.GetValue<string>()
                ?? toolObject["title"]?.GetValue<string>()
                ?? string.Empty;

            var schemaNode = toolObject["inputSchema"] ?? new JsonObject();
            mapped.Add(new ChatConsoleToolDefinition(
                name,
                description,
                JsonSerializer.SerializeToElement(schemaNode)));
        }

        return mapped.Count == 0 ? null : mapped;
    }

    private List<ChatMessage> CreateConversation()
    {
        var conversation = new List<ChatMessage>();
        var systemPrompt = ResolveSystemPrompt();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            conversation.Add(new ChatMessage("system", systemPrompt!));
        }

        return conversation;
    }

    private string? ResolveSystemPrompt()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SystemPrompt))
        {
            return _settings.SystemPrompt;
        }

        if (!string.IsNullOrWhiteSpace(_settings.SystemPromptFilePath))
        {
            var path = Path.GetFullPath(_settings.SystemPromptFilePath);
            return File.ReadAllText(path);
        }

        return null;
    }

    private async Task WriteTranscriptAsync(
        ChatConsoleSessionResult result,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.TranscriptPath))
        {
            return;
        }

        var transcript = new ChatConsoleTranscript
        {
            SessionName = _settings.SessionName,
            RouterBaseUrl = result.RouterBaseUrl,
            Model = result.Model,
            SystemPrompt = result.SystemPrompt,
            Interactive = result.Interactive,
            Streamed = result.Streamed,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            Turns = ConvertTurns(result.Turns)
        };

        var path = Path.GetFullPath(result.TranscriptPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(transcript, _jsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<ChatConsoleTranscriptTurn> ConvertTurns(IReadOnlyList<ChatConsoleTurnResult> turns)
    {
        var transcriptTurns = new ChatConsoleTranscriptTurn[turns.Count];
        for (var index = 0; index < turns.Count; index++)
        {
            var turn = turns[index];
            transcriptTurns[index] = new ChatConsoleTranscriptTurn
            {
                Prompt = turn.Prompt,
                Response = turn.Response,
                FinishReason = turn.FinishReason,
                PromptTokens = turn.PromptTokens,
                CompletionTokens = turn.CompletionTokens,
                TotalTokens = turn.TotalTokens,
                Streamed = turn.Streamed,
                ElapsedMilliseconds = turn.ElapsedMilliseconds
            };
        }

        return transcriptTurns;
    }

    private void WriteTurnSummary(ChatConsoleTurnResult turn)
    {
        var summary = new StringBuilder();
        summary.Append("↳ ");
        var hasPart = false;

        if (!string.IsNullOrWhiteSpace(turn.FinishReason))
        {
            summary.Append("finish=");
            summary.Append(turn.FinishReason);
            hasPart = true;
        }

        if (turn.PromptTokens > 0 || turn.CompletionTokens > 0)
        {
            if (hasPart)
            {
                summary.Append("  ");
            }

            summary.Append("usage=");
            summary.Append(turn.PromptTokens.ToString(CultureInfo.InvariantCulture));
            summary.Append('/');
            summary.Append(turn.CompletionTokens.ToString(CultureInfo.InvariantCulture));
            summary.Append('/');
            summary.Append(turn.TotalTokens.ToString(CultureInfo.InvariantCulture));
            hasPart = true;
        }

        if (turn.ElapsedMilliseconds > 0)
        {
            if (hasPart)
            {
                summary.Append("  ");
            }

            summary.Append(turn.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            summary.Append("ms");
        }

        _output.WriteLine(summary.ToString());
        _output.WriteLine();
    }

    private void WriteMarkdownBlock(string title, string? content)
    {
        var frameWidth = GetFrameWidth();
        var lines = MarkdownConsoleFormatter.RenderMarkdown(content, frameWidth - 4);
        WriteFramedBlock(title, lines, frameWidth, wrapLines: false);
    }

    private void WriteFramedBlock(string title, IReadOnlyList<string> lines, int frameWidth, bool wrapLines = true)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        var preparedLines = wrapLines ? PrepareLines(lines, frameWidth - 4) : new List<string>(lines);
        var widest = ConsoleTextWidth.GetDisplayWidth(title);
        for (var index = 0; index < preparedLines.Count; index++)
        {
            var lineWidth = ConsoleTextWidth.GetDisplayWidth(preparedLines[index]);
            if (lineWidth > widest)
            {
                widest = lineWidth;
            }
        }

        frameWidth = Math.Min(MaxFrameWidth, Math.Max(frameWidth, widest + 4));
        var fillLength = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(title) - 5);
        _output.Write("╭─ ");
        _output.Write(title);
        _output.Write(' ');
        _output.WriteRepeated('─', fillLength);
        _output.WriteLine("╮");

        for (var index = 0; index < preparedLines.Count; index++)
        {
            var line = preparedLines[index];
            var padding = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(line) - 4);
            _output.Write("│ ");
            _output.Write(line);
            _output.WriteRepeated(' ', padding);
            _output.WriteLine(" │");
        }

        _output.Write("╰");
        _output.WriteRepeated('─', Math.Max(0, frameWidth - 2));
        _output.WriteLine("╯");
    }

    private static List<string> PrepareLines(IReadOnlyList<string> lines, int contentWidth)
    {
        var prepared = MarkdownConsoleFormatter.WrapPlainLines(lines, contentWidth);
        return new List<string>(prepared);
    }

    private static string ExtractErrorMessage(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            var json = JsonNode.Parse(body);
            var error = JsonFieldReader.GetString(json, "error", "message");
            if (!string.IsNullOrWhiteSpace(error))
            {
                return error;
            }
        }
        catch (JsonException)
        {
        }

        return body;
    }

    private bool CanLiveRedraw()
    {
        return TerminalCapabilities.SupportsAnsiEscapes(_output);
    }

    private static int GetFrameWidth()
    {
        var terminalWidth = GetTerminalWidth();
        if (terminalWidth > 0)
        {
            return Math.Clamp(terminalWidth - 2, MinFrameWidth, MaxFrameWidth);
        }

        return MinFrameWidth;
    }

    private static int GetTerminalWidth()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return 0;
            }

            var width = Console.WindowWidth;
            return width > 0 ? width : 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class LiveMarkdownPanel
    {
        private readonly TextWriter _writer;
        private readonly string _title;
        private readonly Func<int> _frameWidthFactory;
        private int _renderedLineCount;
        private bool _started;

        public LiveMarkdownPanel(TextWriter writer, string title, Func<int> frameWidthFactory)
        {
            _writer = writer;
            _title = title;
            _frameWidthFactory = frameWidthFactory ?? throw new ArgumentNullException(nameof(frameWidthFactory));
        }

        public void Render(string? content)
        {
            var frameWidth = _frameWidthFactory();
            var lines = MarkdownConsoleFormatter.RenderMarkdown(content, frameWidth - 4);
            Redraw(lines);
        }

        public void Finish(string? content)
        {
            Render(content);
            _writer.WriteLine();
            _writer.Flush();
        }

        private void Redraw(IReadOnlyList<string> contentLines)
        {
            if (_started && _renderedLineCount > 0)
            {
                ClearPreviousRender();
            }

            WriteFrame(contentLines);
            _started = true;
            _renderedLineCount = contentLines.Count + 2;
        }

        private void ClearPreviousRender()
        {
            _writer.Write("\x1b[1A");

            for (var index = 0; index < _renderedLineCount; index++)
            {
                _writer.Write("\x1b[2K");
                if (index < _renderedLineCount - 1)
                {
                    _writer.Write("\x1b[1A");
                }
            }
        }

        private void WriteFrame(IReadOnlyList<string> lines)
        {
            var widest = ConsoleTextWidth.GetDisplayWidth(_title);
            for (var index = 0; index < lines.Count; index++)
            {
                var width = ConsoleTextWidth.GetDisplayWidth(lines[index]);
                if (width > widest)
                {
                    widest = width;
                }
            }
            var frameWidth = Math.Min(MaxFrameWidth, Math.Max(_frameWidthFactory(), widest + 4));
            var fillLength = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(_title) - 5);
            _writer.Write("╭─ ");
            _writer.Write(_title);
            _writer.Write(' ');
            _writer.WriteRepeated('─', fillLength);
            _writer.WriteLine("╮");

            for (var index = 0; index < lines.Count; index++)
            {
                var padding = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(lines[index]) - 4);
                _writer.Write("│ ");
                _writer.Write(lines[index]);
                _writer.WriteRepeated(' ', padding);
                _writer.WriteLine(" │");
            }

            _writer.Write("╰");
            _writer.WriteRepeated('─', Math.Max(0, frameWidth - 2));
            _writer.WriteLine("╯");
            _writer.Flush();
        }
    }

    private sealed class ChatMessage
    {
        public ChatMessage(string role, string content, string? toolCallId = null, IReadOnlyList<ChatConsoleToolCall>? toolCalls = null)
        {
            Role = string.IsNullOrWhiteSpace(role) ? throw new ArgumentException("Role is required.", nameof(role)) : role;
            Content = content ?? string.Empty;
            ToolCallId = string.IsNullOrWhiteSpace(toolCallId) ? null : toolCallId;
            ToolCalls = toolCalls;
        }

        public string Role { get; }

        public string Content { get; }

        public string? ToolCallId { get; }

        public IReadOnlyList<ChatConsoleToolCall>? ToolCalls { get; }
    }

    private sealed class ChatConsoleToolDefinition
    {
        public ChatConsoleToolDefinition(string name, string description, JsonElement inputSchema)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
        }

        public string Name { get; }

        public string Description { get; }

        public JsonElement InputSchema { get; }
    }
}
