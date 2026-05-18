using LanguageExt;
using System.Text.Json;

namespace McpServer.Application.Abstractions.Mcp;

public interface IPromptHandler
{
    string Name { get; }
    string Description { get; }
    PromptDescriptor Describe();
    ValueTask<Fin<GetPromptResult>> GetAsync(JsonElement? arguments, CancellationToken ct);
}

public sealed record PromptDescriptor
{
    public string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<PromptArgumentDescriptor>? Arguments { get; init; }

    public PromptDescriptor(
        string name,
        string? title,
        string? description,
        IReadOnlyList<PromptArgumentDescriptor>? arguments)
    {
        Name = name;
        Title = title;
        Description = description;
        Arguments = arguments;
    }
}

public sealed record PromptArgumentDescriptor
{
    public string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; }

    public PromptArgumentDescriptor(
        string name,
        string? title,
        string? description,
        bool required)
    {
        Name = name;
        Title = title;
        Description = description;
        Required = required;
    }
}

public sealed record GetPromptResult
{
    public string? Description { get; init; }
    public IReadOnlyList<PromptMessage> Messages { get; init; }

    public GetPromptResult(string? description, IReadOnlyList<PromptMessage> messages)
    {
        Description = description;
        Messages = messages;
    }
}

public sealed record PromptMessage
{
    public string Role { get; init; }
    public PromptMessageContent Content { get; init; }

    public PromptMessage(string role, PromptMessageContent content)
    {
        Role = role;
        Content = content;
    }
}

public sealed record PromptMessageContent
{
    public string Type { get; init; }
    public string Text { get; init; }

    public PromptMessageContent(string type, string text)
    {
        Type = type;
        Text = text;
    }

    public static PromptMessageContent FromText(string text) => new("text", text);
}
