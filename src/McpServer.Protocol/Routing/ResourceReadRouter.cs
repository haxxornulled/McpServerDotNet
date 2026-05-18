using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Protocol.Resources;
using McpServer.Protocol.Shared;

namespace McpServer.Protocol.Routing;

public sealed class ResourceReadRouter(IEnumerable<IResourceHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IResourceHandler> _byScheme =
        handlers.ToDictionary(x => x.UriScheme, StringComparer.OrdinalIgnoreCase);

    public ListResourcesResult ListResources() =>
        new(
            Resources: _byScheme.Values
                .Select(x => x.Describe())
                .Select(d => new ResourceDto(
                    d.Name,
                    d.Title,
                    d.Uri,
                    d.Description,
                    d.MimeType,
                    d.Size))
                .ToArray(),
            NextCursor: null);

    public async ValueTask<Fin<ReadResourceResultDto>> RouteAsync(string uri, CancellationToken ct)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return Error.New($"Invalid URI: {uri}");
        }

        if (!_byScheme.TryGetValue(parsed.Scheme, out var handler))
        {
            return Error.New($"No resource handler for scheme: {parsed.Scheme}");
        }

        var appResult = await handler.ReadAsync(uri, ct).ConfigureAwait(false);
        return appResult.Map(ToDto);
    }

    private static ReadResourceResultDto ToDto(ReadResourceResult result)
    {
        var contents = System.Linq.Enumerable
            .Select<ResourceContent, object>(result.Contents, x =>
                x.Text is not null
                    ? new TextResourceContentsDto(x.Uri, x.MimeType, x.Text)
                    : new BlobResourceContentsDto(x.Uri, x.MimeType, x.BlobBase64 ?? string.Empty))
            .ToArray();

        return new ReadResourceResultDto(contents);
    }
}
