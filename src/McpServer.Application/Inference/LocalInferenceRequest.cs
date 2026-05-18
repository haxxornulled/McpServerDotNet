namespace McpServer.Application.Inference;

public sealed class LocalInferenceRequest
{
    public LocalInferenceRequest(
        string operation,
        string prompt,
        string? systemPrompt,
        string? model,
        double? temperature,
        int? maxOutputChars,
        int? timeoutSeconds)
    {
        Operation = string.IsNullOrWhiteSpace(operation) ? "complete" : operation.Trim();
        Prompt = prompt ?? string.Empty;
        SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        Temperature = temperature;
        MaxOutputChars = maxOutputChars;
        TimeoutSeconds = timeoutSeconds;
    }

    public string Operation { get; }

    public string Prompt { get; }

    public string? SystemPrompt { get; }

    public string? Model { get; }

    public double? Temperature { get; }

    public int? MaxOutputChars { get; }

    public int? TimeoutSeconds { get; }
}
