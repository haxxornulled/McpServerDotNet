using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Protocol.Prompts;

namespace McpServer.Protocol.Routing;

public sealed class PromptRouter(IEnumerable<IPromptHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IPromptHandler> _byName =
        handlers.ToDictionary(x => x.Name, StringComparer.Ordinal);

    public ListPromptsResult ListPrompts() =>
        new(
            Prompts: _byName.Values
                .Select(x => x.Describe())
                .Select(d => new PromptDto(
                    d.Name,
                    d.Title,
                    d.Description,
                    d.Arguments?.Select(a => new PromptArgumentDto(
                        a.Name,
                        a.Title,
                        a.Description,
                        a.Required)).ToArray()))
                .ToArray(),
            NextCursor: null);

    public async ValueTask<Fin<GetPromptResultDto>> GetAsync(string name, JsonElement? arguments, CancellationToken ct)
    {
        if (!_byName.TryGetValue(name, out var handler))
        {
            return Error.New($"Unknown prompt: {name}");
        }

        var result = await handler.GetAsync(arguments, ct).ConfigureAwait(false);
        return result.Map(ToDto);
    }

    private static GetPromptResultDto ToDto(GetPromptResult result) =>
        new(
            result.Description,
            result.Messages
                .Select(m => new PromptMessageDto(
                    m.Role,
                    PromptMessageContentDto.FromText(m.Content.Text)))
                .ToArray());
}
