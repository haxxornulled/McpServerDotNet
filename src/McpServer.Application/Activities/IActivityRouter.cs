namespace McpServer.Application.Activities;

public interface IActivityRouter
{
    ValueTask<ActivityRoutingResult> RouteAsync(string userRequest, CancellationToken cancellationToken = default);
}
