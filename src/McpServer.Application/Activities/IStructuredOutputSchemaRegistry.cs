using LanguageExt;
namespace McpServer.Application.Activities;

public interface IStructuredOutputSchemaRegistry
{
    IReadOnlyList<StructuredOutputSchemaDto> ListSchemas();
    Fin<StructuredOutputSchemaDto> GetSchema(string schemaName);
    Fin<StructuredOutputSchemaDto> GetSchema(ActivityKind activity);
}
