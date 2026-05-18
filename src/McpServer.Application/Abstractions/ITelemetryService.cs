using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITelemetryService
    {
        ValueTask<Fin<Unit>> TrackEventAsync(string eventName, IDictionary<string, string> properties, CancellationToken ct);
        ValueTask<Fin<Unit>> TrackExceptionAsync(Exception ex, IDictionary<string, string> properties, CancellationToken ct);
    }
}