using System.Text;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Files.Commands;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Resources;

public sealed class FsFileTextResourceHandler(
    IFileSystemService fileSystemService,
    IResourcePathTranslator resourcePathTranslator,
    ILogger<FsFileTextResourceHandler> logger) : IResourceHandler
{
    public string UriScheme => "file";
    public string Name => "file";
    public string Description => "Reads text file content from allowed roots.";

    public ResourceDescriptor Describe() =>
        new("file", "File text resource", "file:///workspace/example.txt", Description, "text/plain");

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
            .ReadTextAsync(new ReadFileTextCommand(localPath, Encoding.UTF8.WebName), ct)
            .ConfigureAwait(false);

        return result.Map(r =>
        {
            logger.LogInformation("Resource read completed for {Uri}", uri);
            return new ReadResourceResult([new ResourceContent(uri, "text/plain", text: r.Content)]);
        });
    }
}
