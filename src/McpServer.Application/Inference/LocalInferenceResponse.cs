namespace McpServer.Application.Inference;

public sealed class LocalInferenceResponse
{
    public LocalInferenceResponse(
        string provider,
        string model,
        string operation,
        string response,
        int promptChars,
        int responseChars,
        bool truncated,
        long elapsedMilliseconds)
    {
        Provider = provider;
        Model = model;
        Operation = operation;
        Response = response;
        PromptChars = promptChars;
        ResponseChars = responseChars;
        Truncated = truncated;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public string Provider { get; }

    public string Model { get; }

    public string Operation { get; }

    public string Response { get; }

    public int PromptChars { get; }

    public int ResponseChars { get; }

    public bool Truncated { get; }

    public long ElapsedMilliseconds { get; }
}
