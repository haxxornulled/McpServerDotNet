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
            Prompts: BuildPromptDtos(),
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
            BuildMessageDtos(result.Messages));

    private static PromptMessageDto[] BuildMessageDtos(IReadOnlyList<PromptMessage> messages)
    {
        var items = new PromptMessageDto[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            items[i] = new PromptMessageDto(
                messages[i].Role,
                PromptMessageContentDto.FromText(messages[i].Content.Text));
        }

        return items;
    }

    private PromptDto[] BuildPromptDtos()
    {
        var prompts = new PromptDto[_byName.Count];
        var index = 0;
        foreach (var prompt in _byName.Values)
        {
            var descriptor = prompt.Describe();
            prompts[index++] = new PromptDto(
                descriptor.Name,
                descriptor.Title,
                descriptor.Description,
                BuildArgumentDtos(descriptor.Arguments));
        }

        return prompts;
    }

    private static PromptArgumentDto[]? BuildArgumentDtos(IReadOnlyList<PromptArgumentDescriptor>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        var items = new PromptArgumentDto[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            items[i] = new PromptArgumentDto(argument.Name, argument.Title, argument.Description, argument.Required);
        }

        return items;
    }
}
