using LanguageExt;

namespace McpServer.Application.Abstractions.Mcp;

public interface IResourceHandler
{
    string UriScheme { get; }
    string Name { get; }
    string Description { get; }
    ValueTask<Fin<ReadResourceResult>> ReadAsync(string uri, CancellationToken ct);
    ResourceDescriptor Describe();
}

public sealed record ReadResourceResult
{
    public IReadOnlyList<ResourceContent> Contents { get; init; }

    public ReadResourceResult(IReadOnlyList<ResourceContent> contents)
    {
        Contents = contents;
    }
}

public sealed record ResourceContent
{
    public string Uri { get; init; }
    public string MimeType { get; init; }
    public string? Text { get; init; }
    public string? BlobBase64 { get; init; }

    public ResourceContent(
        string uri,
        string mimeType,
        string? text = null,
        string? blobBase64 = null)
    {
        Uri = uri;
        MimeType = mimeType;
        Text = text;
        BlobBase64 = blobBase64;
    }
}

public sealed record ResourceDescriptor
{
    public string Name { get; init; }
    public string? Title { get; init; }
    public string Uri { get; init; }
    public string? Description { get; init; }
    public string? MimeType { get; init; }
    public long? Size { get; init; }

    public ResourceDescriptor(
        string name,
        string? title,
        string uri,
        string? description,
        string? mimeType = null,
        long? size = null)
    {
        Name = name;
        Title = title;
        Uri = uri;
        Description = description;
        MimeType = mimeType;
        Size = size;
    }
}
