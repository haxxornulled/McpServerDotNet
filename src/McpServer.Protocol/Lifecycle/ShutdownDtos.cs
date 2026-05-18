namespace McpServer.Protocol.Lifecycle;

public sealed class ShutdownRequestDto
{
    public static ShutdownRequestDto Instance { get; } = new();

    public ShutdownRequestDto()
    {
    }
}
