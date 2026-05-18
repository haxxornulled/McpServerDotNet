using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class ProviderUnavailableProbe
{
    private readonly HttpClient _httpClient;
    private readonly Uri _routerBaseUrl;
    private readonly string _chatModel;
    private readonly bool _strict;

    public ProviderUnavailableProbe(HttpClient httpClient, Uri routerBaseUrl, string chatModel, bool strict)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routerBaseUrl = routerBaseUrl ?? throw new ArgumentNullException(nameof(routerBaseUrl));
        _chatModel = string.IsNullOrWhiteSpace(chatModel) ? "fast-local" : chatModel;
        _strict = strict;
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        ConsoleWriter.WriteSection("Provider unavailable probe");

        var body = new
        {
            model = _chatModel,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Reply with exactly: router online"
                }
            },
            stream = false
        };

        var response = await HttpJson.PostAsync(_httpClient, new Uri(_routerBaseUrl, "/v1/chat/completions"), body, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == 0)
        {
            ConsoleWriter.WriteError(
                $"AgentRouter is not reachable at {_routerBaseUrl}. Start McpServer.AgentRouter.Host first, then rerun this probe. Transport error: {response.ErrorMessage}");
            return false;
        }

        var code = JsonFieldReader.GetString(response.Json, "error", "code");
        var type = JsonFieldReader.GetString(response.Json, "error", "type");

        if (response.StatusCode == 503
            && string.Equals(code, "provider_unavailable", StringComparison.OrdinalIgnoreCase)
            && string.Equals(type, "service_unavailable_error", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleWriter.WritePass("Provider unavailable returned HTTP 503 with provider_unavailable envelope.");
            return true;
        }

        if (response.Success)
        {
            ConsoleWriter.WriteWarning(
                "Provider-unavailable precondition was not met: AgentRouter returned a successful chat completion. "
                + "Ollama is reachable. Stop Ollama after AgentRouter has started, or run AgentRouter with startup Ollama auto-start disabled, then rerun this probe.");

            return !_strict;
        }

        ConsoleWriter.WriteError($"Expected HTTP 503 provider_unavailable, got HTTP {response.StatusCode.ToString(CultureInfo.InvariantCulture)} code '{code}' type '{type}'.");
        return false;
    }
}
