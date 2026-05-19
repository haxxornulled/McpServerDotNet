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
    private const int FrameWidth = 78;
    private const int FrameContentWidth = FrameWidth - 4;

    private readonly ChatConsoleSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly bool _inputRedirected;
    private readonly JsonSerializerOptions _jsonOptions = JsonOptions.CreateIndented();

    public ChatConsoleRunner(
        ChatConsoleSettings settings,
        HttpClient httpClient,
        TextReader input,
        TextWriter output,
        TextWriter error,
        bool inputRedirected)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _inputRedirected = inputRedirected;
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

            var line = _input.ReadLine();
            if (line is null)
            {
                break;
            }

            if (IsExitCommand(line))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await RunOneTurnAsync(conversation, turns, line, streamed, cancellationToken).ConfigureAwait(false);
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
            var result = streamed
                ? await ExecuteStreamingTurnAsync(conversation, prompt, cancellationToken).ConfigureAwait(false)
                : await ExecuteNonStreamingTurnAsync(conversation, prompt, cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                conversation.RemoveAt(conversation.Count - 1);
                return null;
            }

            conversation.Add(new ChatMessage("assistant", result.Response));
            return result;
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

    private async Task<ChatConsoleTurnResult?> ExecuteNonStreamingTurnAsync(
        IReadOnlyList<ChatMessage> conversation,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(conversation, stream: false);
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

        if (!CliOutput.IsJson)
        {
            WriteAssistantPanel(assistant);
        }

        return new ChatConsoleTurnResult(
            prompt,
            assistant,
            finishReason,
            promptTokens,
            completionTokens,
            promptTokens + completionTokens,
            streamed: false,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<ChatConsoleTurnResult?> ExecuteStreamingTurnAsync(
        IReadOnlyList<ChatMessage> conversation,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(conversation, stream: true);
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
            ? new LiveMarkdownPanel(_output, "Assistant")
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

    private HttpRequestMessage BuildRequest(
        IReadOnlyList<ChatMessage> conversation,
        bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_settings.RouterBaseUrl, "/v1/chat/completions"));

        var messages = new object[conversation.Count];
        for (var index = 0; index < conversation.Count; index++)
        {
            var message = conversation[index];
            messages[index] = new
            {
                role = message.Role,
                content = message.Content
            };
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = _settings.Model,
            ["messages"] = messages,
            ["stream"] = stream
        };

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

        WriteFramedBlock("Chat Console", lines);
        _output.WriteLine("Type 'exit' or 'quit' to leave.");
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
        var lines = MarkdownConsoleFormatter.RenderMarkdown(content, FrameContentWidth);
        WriteFramedBlock(title, lines, wrapLines: false);
    }

    private void WriteFramedBlock(string title, IReadOnlyList<string> lines, bool wrapLines = true)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        var preparedLines = wrapLines ? PrepareLines(lines) : new List<string>(lines);
        var widest = ConsoleTextWidth.GetDisplayWidth(title);
        for (var index = 0; index < preparedLines.Count; index++)
        {
            var lineWidth = ConsoleTextWidth.GetDisplayWidth(preparedLines[index]);
            if (lineWidth > widest)
            {
                widest = lineWidth;
            }
        }

        var frameWidth = Math.Min(120, Math.Max(FrameWidth, widest + 4));
        var fillLength = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(title) - 5);
        _output.WriteLine($"╭─ {title} {new string('─', fillLength)}╮");

        for (var index = 0; index < preparedLines.Count; index++)
        {
            var line = preparedLines[index];
            var padding = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(line) - 4);
            _output.WriteLine($"│ {line}{new string(' ', padding)} │");
        }

        _output.WriteLine($"╰{new string('─', Math.Max(0, frameWidth - 2))}╯");
    }

    private static List<string> PrepareLines(IReadOnlyList<string> lines)
    {
        var prepared = MarkdownConsoleFormatter.WrapPlainLines(lines, FrameContentWidth);
        return new List<string>(prepared);
    }

    private static bool IsExitCommand(string value)
    {
        return string.Equals(value.Trim(), "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "quit", StringComparison.OrdinalIgnoreCase);
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

    private sealed class LiveMarkdownPanel
    {
        private readonly TextWriter _writer;
        private readonly string _title;
        private readonly int _frameWidth;
        private int _renderedLineCount;
        private bool _started;

        public LiveMarkdownPanel(TextWriter writer, string title)
        {
            _writer = writer;
            _title = title;
            _frameWidth = FrameWidth;
        }

        public void Render(string? content)
        {
            var lines = MarkdownConsoleFormatter.RenderMarkdown(content, _frameWidth - 4);
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

            var frameWidth = Math.Min(120, Math.Max(FrameWidth, widest + 4));
            var fillLength = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(_title) - 5);
            _writer.WriteLine($"╭─ {_title} {new string('─', fillLength)}╮");

            for (var index = 0; index < lines.Count; index++)
            {
                var padding = Math.Max(0, frameWidth - ConsoleTextWidth.GetDisplayWidth(lines[index]) - 4);
                _writer.WriteLine($"│ {lines[index]}{new string(' ', padding)} │");
            }

            _writer.WriteLine($"╰{new string('─', Math.Max(0, frameWidth - 2))}╯");
            _writer.Flush();
        }
    }

    private sealed class ChatMessage
    {
        public ChatMessage(string role, string content)
        {
            Role = string.IsNullOrWhiteSpace(role) ? throw new ArgumentException("Role is required.", nameof(role)) : role;
            Content = content ?? string.Empty;
        }

        public string Role { get; }

        public string Content { get; }
    }
}
