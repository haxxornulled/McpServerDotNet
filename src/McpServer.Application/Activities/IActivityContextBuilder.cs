namespace McpServer.Application.Activities;

public interface IActivityContextBuilder
{
    ValueTask<ActivityContextPacket> BuildAsync(
        ActivityRoutingResult routing,
        ActivityProfileDto profile,
        string userRequest,
        int? maxContextBytes = null,
        CancellationToken cancellationToken = default);
}
