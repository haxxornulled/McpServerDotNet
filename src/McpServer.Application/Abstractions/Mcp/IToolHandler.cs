using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;

namespace McpServer.Application.Abstractions.Mcp
{
    public interface IToolHandler
    {
        string Name { get; }
        string Description { get; }
        JsonElement GetInputSchema();

        ValueTask<Fin<CallToolResult>> Handle(JsonElement arguments, CancellationToken ct);
    }

    public interface IToolHandler<TRequest> : IToolHandler
    {
        ValueTask<Fin<CallToolResult>> Handle(TRequest request, CancellationToken ct);

        IReadOnlyList<string> Validate(TRequest request) => [];

        async ValueTask<Fin<CallToolResult>> IToolHandler.Handle(JsonElement arguments, CancellationToken ct)
        {
            JsonDocument? emptyArguments = null;

            try
            {
                if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                {
                    emptyArguments = JsonDocument.Parse("{}");
                }

                var effectiveArguments = emptyArguments?.RootElement ?? arguments;
                var schemaErrors = ToolArgumentSchemaValidator.Validate(Name, effectiveArguments, GetInputSchema());
                if (schemaErrors.Count > 0)
                {
                    return Error.New(string.Join(" ", schemaErrors));
                }

                var request = effectiveArguments.Deserialize<TRequest>();
                if (request is null)
                {
                    return Error.New($"Invalid arguments for {Name}");
                }

                var validationErrors = Validate(request);
                if (validationErrors.Count > 0)
                {
                    return Error.New(string.Join(" ", validationErrors));
                }

                return await Handle(request, ct).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return Error.New($"Invalid JSON arguments for {Name}: {ex.Message}");
            }
            catch (NotSupportedException ex)
            {
                return Error.New($"Unsupported arguments for {Name}: {ex.Message}");
            }
            finally
            {
                emptyArguments?.Dispose();
            }
        }
    }

    public record CallToolResult(
        IReadOnlyList<ContentItem> Content,
        object? StructuredContent = null,
        bool IsError = false);

    public record ContentItem(
        string Type,
        string Text);
}
