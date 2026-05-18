using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Protocol.Shared;
using McpServer.Protocol.Tools;

namespace McpServer.Protocol.Routing;

public sealed class ToolCallRouter
{
    private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;
    private readonly ToolDto[] _tools;

    public ToolCallRouter(IEnumerable<IToolHandler> handlers)
    {
        var handlerArray = handlers
            .OrderBy(static handler => handler.Name, StringComparer.Ordinal)
            .ToArray();

        var duplicateNames = handlerArray
            .GroupBy(static handler => handler.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate MCP tool names are registered: {string.Join(", ", duplicateNames)}");
        }

        _handlers = handlerArray.ToDictionary(static handler => handler.Name, StringComparer.Ordinal);
        _tools = handlerArray.Select(ToToolDto).ToArray();
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

        var appResult = await handler.Handle(arguments, ct).ConfigureAwait(false);
        return appResult.Match<Fin<CallToolResultDto>>(
            Succ: result => Fin<CallToolResultDto>.Succ(ToCallToolDto(result)),
            Fail: error => ToToolErrorDto(name, "tool_execution_failed", error.Message));
    }

    private static ToolDto ToToolDto(IToolHandler handler) =>
        new(Name: handler.Name, Title: null, Description: handler.Description, InputSchema: handler.GetInputSchema());

    private static CallToolResultDto ToCallToolDto(CallToolResult result) =>
        new(
            Content: result.Content.Select(x => TextContentDto.Create(x.Text)).ToArray(),
            StructuredContent: result.StructuredContent,
            IsError: result.IsError);

    private static Fin<CallToolResultDto> ToToolErrorDto(string toolName, string errorCode, string message)
    {
        var error = new ToolErrorDto(Tool: toolName, Success: false, ErrorCode: errorCode, Message: message);
        return Fin<CallToolResultDto>.Succ(new CallToolResultDto(
            Content: [TextContentDto.Create(message)],
            StructuredContent: error,
            IsError: true));
    }
}
