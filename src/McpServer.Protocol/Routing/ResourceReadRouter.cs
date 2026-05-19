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
            Resources: BuildResourceDtos(),
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
        var contents = new object[result.Contents.Count];
        for (var i = 0; i < result.Contents.Count; i++)
        {
            var item = result.Contents[i];
            contents[i] = item.Text is not null
                ? new TextResourceContentsDto(item.Uri, item.MimeType, item.Text)
                : new BlobResourceContentsDto(item.Uri, item.MimeType, item.BlobBase64 ?? string.Empty);
        }

        return new ReadResourceResultDto(contents);
    }

    private ResourceDto[] BuildResourceDtos()
    {
        var resources = new ResourceDto[_byScheme.Count];
        var index = 0;
        foreach (var resource in _byScheme.Values)
        {
            var descriptor = resource.Describe();
            resources[index++] = new ResourceDto(
                descriptor.Name,
                descriptor.Title,
                descriptor.Uri,
                descriptor.Description,
                descriptor.MimeType,
                descriptor.Size);
        }

        return resources;
    }
}
