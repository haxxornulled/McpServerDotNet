using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Protocol.Shared;
using McpServer.Protocol.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Protocol.Routing;

public sealed class ToolCallRouter
{
    private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;
    private readonly ToolDto[] _tools;
    private readonly ILogger<ToolCallRouter> _logger;

    public ToolCallRouter(IEnumerable<IToolHandler> handlers, ILogger<ToolCallRouter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var handlerList = new List<IToolHandler>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            handlerList.Add(handler);
            if (seen.TryGetValue(handler.Name, out var count))
            {
                seen[handler.Name] = count + 1;
            }
            else
            {
                seen[handler.Name] = 1;
            }
        }

        var duplicateNames = new List<string>();
        foreach (var pair in seen)
        {
            if (pair.Value > 1)
            {
                duplicateNames.Add(pair.Key);
            }
        }

        if (duplicateNames.Count > 0)
        {
            duplicateNames.Sort(StringComparer.Ordinal);
            throw new InvalidOperationException($"Duplicate MCP tool names are registered: {string.Join(", ", duplicateNames)}");
        }

        handlerList.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        _handlers = handlerList.ToDictionary(static handler => handler.Name, StringComparer.Ordinal);
        _tools = new ToolDto[handlerList.Count];
        for (var i = 0; i < handlerList.Count; i++)
        {
            _tools[i] = ToToolDto(handlerList[i]);
        }
    }

    public ListToolsResult ListTools()
    {
        return new ListToolsResult(Tools: _tools, NextCursor: null);
    }

    public async ValueTask<Fin<CallToolResultDto>> RouteAsync(string name, JsonElement arguments, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ToToolErrorDto("unknown", "invalid_tool_name", "Tool name is required.");
        }

        if (!_handlers.TryGetValue(name, out var handler))
        {
            return ToToolErrorDto(name, "unknown_tool", $"Unknown tool: {name}");
        }

        try
        {
            var appResult = await handler.Handle(arguments, ct).ConfigureAwait(false);
            return appResult.Match<Fin<CallToolResultDto>>(
                Succ: result => Fin<CallToolResultDto>.Succ(ToCallToolDto(result)),
                Fail: error => ToToolErrorDto(name, "tool_execution_failed", error.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while executing MCP tool {ToolName}", name);
            return ToToolErrorDto(name, "tool_execution_failed", ex.Message);
        }
    }

    private static ToolDto ToToolDto(IToolHandler handler) =>
        new(Name: handler.Name, Title: null, Description: handler.Description, InputSchema: handler.GetInputSchema());

    private static CallToolResultDto ToCallToolDto(CallToolResult result) =>
        new(
            Content: ToTextContentArray(result.Content),
            StructuredContent: result.StructuredContent,
            IsError: result.IsError);

    private static TextContentDto[] ToTextContentArray(IReadOnlyList<ContentItem> content)
    {
        var items = new TextContentDto[content.Count];
        for (var i = 0; i < content.Count; i++)
        {
            items[i] = TextContentDto.Create(content[i].Text);
        }

        return items;
    }

    private static Fin<CallToolResultDto> ToToolErrorDto(string toolName, string errorCode, string message)
    {
        var error = new ToolErrorDto(Tool: toolName, Success: false, ErrorCode: errorCode, Message: message);
        return Fin<CallToolResultDto>.Succ(new CallToolResultDto(
            Content: [TextContentDto.Create(message)],
            StructuredContent: error,
            IsError: true));
    }
}
