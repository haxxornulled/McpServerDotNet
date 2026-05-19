using System.Buffers;
using System.Text;

namespace McpServer.Infrastructure.Ssh;

public sealed record SshCommandStreamCapture(
    string StandardOutput,
    string StandardError,
    bool StdoutTruncated,
    bool StderrTruncated,
    Exception? ExecutionException);

public static class SshCommandStreamPump
{
    private const int DefaultBufferSize = 4096;

    public static async ValueTask<SshCommandStreamCapture> CaptureAsync(
        Stream stdoutStream,
        Stream stderrStream,
        Func<CancellationToken, Task> executeAsync,
        int maxOutputChars,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stdoutStream);
        ArgumentNullException.ThrowIfNull(stderrStream);
        ArgumentNullException.ThrowIfNull(executeAsync);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stdoutState = new StreamCaptureState(maxOutputChars);
        var stderrState = new StreamCaptureState(maxOutputChars);

        var stdoutTask = DrainAsync(stdoutStream, stdoutState, linkedCts.Token);
        var stderrTask = DrainAsync(stderrStream, stderrState, linkedCts.Token);

        Exception? executionException = null;
        try
        {
            await executeAsync(linkedCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            linkedCts.Cancel();
            await DrainReadersAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            executionException = ex;
            linkedCts.Cancel();
            await DrainReadersAsync(stdoutTask, stderrTask).ConfigureAwait(false);
        }

        return new SshCommandStreamCapture(
            stdoutState.ToString(),
            stderrState.ToString(),
            stdoutState.Truncated,
            stderrState.Truncated,
            executionException);
    }

    private static async Task DrainReadersAsync(Task stdoutTask, Task stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException aggregateException) when (AllCanceled(aggregateException))
        {
        }
    }

    private static bool AllCanceled(AggregateException aggregateException)
    {
        foreach (var innerException in aggregateException.InnerExceptions)
        {
            if (innerException is not OperationCanceledException)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task DrainAsync(
        Stream stream,
        StreamCaptureState state,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: DefaultBufferSize,
            leaveOpen: true);

        var buffer = ArrayPool<char>.Shared.Rent(DefaultBufferSize);
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    return;
                }

                state.Append(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private sealed class StreamCaptureState
    {
        private readonly int _maxChars;
        private readonly StringBuilder _builder = new();
        private int _capturedChars;

        public StreamCaptureState(int maxChars)
        {
            _maxChars = Math.Max(1, maxChars);
        }

        public bool Truncated { get; private set; }

        public void Append(ReadOnlySpan<char> chars)
        {
            if (_capturedChars >= _maxChars)
            {
                Truncated = true;
                return;
            }

            var remaining = _maxChars - _capturedChars;
            var toWrite = Math.Min(remaining, chars.Length);
            _builder.Append(chars[..toWrite]);
            _capturedChars += toWrite;

            if (toWrite < chars.Length)
            {
                Truncated = true;
            }
        }

        public override string ToString() => _builder.ToString();
    }
}
