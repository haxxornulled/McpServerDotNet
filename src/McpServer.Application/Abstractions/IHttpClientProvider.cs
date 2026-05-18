using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IHttpClientProvider
    {
        ValueTask<Fin<HttpResponseMessage>> GetAsync(string url, CancellationToken ct);
        ValueTask<Fin<HttpResponseMessage>> PostAsync(string url, HttpContent content, CancellationToken ct);
        ValueTask<Fin<HttpResponseMessage>> PutAsync(string url, HttpContent content, CancellationToken ct);
        ValueTask<Fin<HttpResponseMessage>> DeleteAsync(string url, CancellationToken ct);
    }
}