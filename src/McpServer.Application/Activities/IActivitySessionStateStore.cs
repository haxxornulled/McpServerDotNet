namespace McpServer.Application.Activities;

public interface IActivitySessionStateStore
{
    ActivitySessionState Current { get; }
    ActivitySessionState Update(ActivityKind activity, string userRequest, string schemaName, string summary);
}
