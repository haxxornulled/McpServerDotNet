using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Files.Commands;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Resources;

public sealed class FsFileMetadataResourceHandler(
    IFileSystemService fileSystemService,
    IResourcePathTranslator resourcePathTranslator,
    ILogger<FsFileMetadataResourceHandler> logger) : IResourceHandler
{
    public string UriScheme => "filemeta";
    public string Name => "filemeta";
    public string Description => "Reads file or directory metadata within allowed roots.";

    public ResourceDescriptor Describe() =>
        new("filemeta", "File metadata resource", "filemeta:///workspace/example.txt", Description, "application/json");

    public async ValueTask<Fin<ReadResourceResult>> ReadAsync(string uri, CancellationToken ct)
    {
        var translated = resourcePathTranslator.TryTranslateToLocalPath(uri);
        if (translated.IsFail)
        {
            return translated.Match<Fin<ReadResourceResult>>(
                Succ: _ => throw new InvalidOperationException("Expected resource path translation to fail."),
                Fail: error => error);
        }

        var localPath = translated.Match(
            Succ: path => path,
            Fail: error => throw new InvalidOperationException(error.Message));

        var result = await fileSystemService
            .GetMetadataAsync(new GetMetadataCommand(localPath), ct)
            .ConfigureAwait(false);

        return result.Map(r =>
        {
            var json = JsonSerializer.Serialize(r, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            logger.LogInformation("Metadata resource read completed for {Uri}", uri);
            return new ReadResourceResult([new ResourceContent(uri, "application/json", text: json)]);
        });
    }
}
