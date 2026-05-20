using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal enum ChatConsoleInputKind
{
    Prompt,
    PasteModeStarted,
    ToolCommand,
    Exit,
    EndOfStream
}

internal sealed class ChatConsoleInputSubmission
{
    public ChatConsoleInputSubmission(ChatConsoleInputKind kind, string? prompt, string? notice = null, string? toolName = null)
    {
        Kind = kind;
        Prompt = prompt;
        Notice = notice;
        ToolName = toolName;
    }

    public ChatConsoleInputKind Kind { get; }

    public string? Prompt { get; }

    public string? Notice { get; }

    public string? ToolName { get; }
}

internal sealed class ChatConsoleInputController
{
    private const string PasteModeCommand = "/paste";
    private const string PasteEndCommand = "/end";
    private const string SearchCommand = "/search";
    private const string SearchToolName = "web.search";

    private readonly TextReader _input;
    private readonly IClipboardTextProvider _clipboardTextProvider;
    private bool _manualPasteMode;

    public ChatConsoleInputController(TextReader input)
        : this(input, new PowerShellClipboardTextProvider())
    {
    }

    public ChatConsoleInputController(TextReader input, IClipboardTextProvider clipboardTextProvider)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _clipboardTextProvider = clipboardTextProvider ?? throw new ArgumentNullException(nameof(clipboardTextProvider));
    }

    public async Task<ChatConsoleInputSubmission> ReadNextSubmissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var line = await _input.ReadLineAsync().ConfigureAwait(false);
        if (line is null)
        {
            return new ChatConsoleInputSubmission(ChatConsoleInputKind.EndOfStream, null);
        }

        if (_manualPasteMode)
        {
            if (IsPasteEndCommand(line))
            {
                _manualPasteMode = false;
                return new ChatConsoleInputSubmission(ChatConsoleInputKind.Prompt, null, "Paste mode ended.");
            }

            var pastedPrompt = await CaptureManualPasteAsync(line, cancellationToken).ConfigureAwait(false);
            _manualPasteMode = false;
            return string.IsNullOrWhiteSpace(pastedPrompt)
                ? new ChatConsoleInputSubmission(ChatConsoleInputKind.Prompt, null, "Paste mode ended with no content.")
                : new ChatConsoleInputSubmission(ChatConsoleInputKind.Prompt, pastedPrompt, "Paste mode submitted.");
        }

        if (IsExitCommand(line))
        {
            return new ChatConsoleInputSubmission(ChatConsoleInputKind.Exit, null);
        }

        if (IsPasteCommand(line))
        {
            var clipboardText = await _clipboardTextProvider.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(clipboardText))
            {
                return new ChatConsoleInputSubmission(ChatConsoleInputKind.Prompt, NormalizeLineEndings(clipboardText), "Clipboard pasted.");
            }

            _manualPasteMode = true;
            return new ChatConsoleInputSubmission(ChatConsoleInputKind.PasteModeStarted, null, "Paste mode active. Paste text, then finish with /end.");
        }

        if (TryParseSearchCommand(line, out var query, out var notice))
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                var clipboardText = await _clipboardTextProvider.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(clipboardText))
                {
                    return new ChatConsoleInputSubmission(
                        ChatConsoleInputKind.ToolCommand,
                        NormalizeLineEndings(clipboardText),
                        "Clipboard search submitted.",
                        SearchToolName);
                }

                return new ChatConsoleInputSubmission(
                    ChatConsoleInputKind.ToolCommand,
                    null,
                    "Usage: /search <query> or copy text and run /search.",
                    SearchToolName);
            }

            return new ChatConsoleInputSubmission(ChatConsoleInputKind.ToolCommand, query, notice, SearchToolName);
        }

        return string.IsNullOrWhiteSpace(line)
            ? new ChatConsoleInputSubmission(ChatConsoleInputKind.Prompt, null)
            : new ChatConsoleInputSubmission(ChatConsoleInputKind.Prompt, line);
    }

    public static bool IsPasteCommand(string value)
    {
        return string.Equals(value.Trim(), PasteModeCommand, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSearchCommand(string value)
    {
        return value.TrimStart().StartsWith(SearchCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSearchCommand(string line, out string? query, out string? notice)
    {
        query = null;
        notice = null;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(SearchCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        query = trimmed[SearchCommand.Length..].Trim();
        notice = string.IsNullOrWhiteSpace(query)
            ? "Usage: /search <query> or copy text and run /search."
            : "Web search submitted.";
        return true;
    }

    private async Task<string?> CaptureManualPasteAsync(string firstLine, CancellationToken cancellationToken)
    {
        var lines = new List<string> { firstLine };
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await _input.ReadLineAsync().ConfigureAwait(false);
            if (line is null || IsPasteEndCommand(line))
            {
                break;
            }

            lines.Add(line);
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static bool IsExitCommand(string value)
    {
        return string.Equals(value.Trim(), "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "quit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPasteEndCommand(string value)
    {
        return string.Equals(value.Trim(), PasteEndCommand, StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IClipboardTextProvider
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken);
}

internal sealed class PowerShellClipboardTextProvider : IClipboardTextProvider
{
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return await TryReadClipboardAsync("pwsh.exe", cancellationToken).ConfigureAwait(false)
            ?? await TryReadClipboardAsync("powershell.exe", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> TryReadClipboardAsync(string executable, CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add("Get-Clipboard -Raw");

            if (!process.Start())
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (process is not null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            return null;
        }
        catch
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
            }

            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }
}
