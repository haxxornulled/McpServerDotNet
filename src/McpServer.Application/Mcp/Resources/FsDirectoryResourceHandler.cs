using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Files.Commands;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Resources;

public sealed class FsDirectoryResourceHandler(
    IFileSystemService fileSystemService,
    IResourcePathTranslator resourcePathTranslator,
    ILogger<FsDirectoryResourceHandler> logger) : IResourceHandler
{
    public string UriScheme => "dir";
    public string Name => "dir";
    public string Description => "Lists a directory within allowed roots.";

    public ResourceDescriptor Describe() =>
        new("dir", "Directory listing resource", "dir:///workspace", Description, "application/json");

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
            .ListDirectoryAsync(new ListDirectoryCommand(localPath), ct)
            .ConfigureAwait(false);

        return result.Map(r =>
        {
            var json = JsonSerializer.Serialize(r, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            logger.LogInformation("Directory resource read completed for {Uri}", uri);
            return new ReadResourceResult([new ResourceContent(uri, "application/json", text: json)]);
        });
    }
}
