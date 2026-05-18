using System.Text.Json;

namespace McpServer.Application.Abstractions.Mcp;

public static class ToolArgumentSchemaValidator
{
    public static IReadOnlyList<string> Validate(string toolName, JsonElement arguments, JsonElement inputSchema)
    {
        var errors = new List<string>();

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Tool '{toolName}' arguments must be a JSON object.");
            return errors;
        }

        var propertyNames = GetSchemaPropertyNames(inputSchema);
        var requiredNames = GetRequiredPropertyNames(inputSchema);

        foreach (var requiredName in requiredNames)
        {
            if (!arguments.TryGetProperty(requiredName, out var value) || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                errors.Add($"Missing required argument '{requiredName}' for tool '{toolName}'.");
            }
        }

        var rejectsAdditionalProperties = inputSchema.TryGetProperty("additionalProperties", out var additionalPropertiesElement) &&
            additionalPropertiesElement.ValueKind == JsonValueKind.False;

        if (rejectsAdditionalProperties)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                if (!propertyNames.Contains(property.Name))
                {
                    errors.Add($"Unexpected argument '{property.Name}' for tool '{toolName}'.");
                }
            }
        }

        return errors;
    }

    private static System.Collections.Generic.HashSet<string> GetSchemaPropertyNames(JsonElement inputSchema)
    {
        var propertyNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        if (!inputSchema.TryGetProperty("properties", out var propertiesElement) || propertiesElement.ValueKind != JsonValueKind.Object)
        {
            return propertyNames;
        }

        foreach (var property in propertiesElement.EnumerateObject())
        {
            propertyNames.Add(property.Name);
        }

        return propertyNames;
    }

    private static System.Collections.Generic.HashSet<string> GetRequiredPropertyNames(JsonElement inputSchema)
    {
        var requiredNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        if (!inputSchema.TryGetProperty("required", out var requiredElement) || requiredElement.ValueKind != JsonValueKind.Array)
        {
            return requiredNames;
        }

        foreach (var item in requiredElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } requiredName)
            {
                requiredNames.Add(requiredName);
            }
        }

        return requiredNames;
    }
}
