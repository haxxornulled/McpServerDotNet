namespace McpServer.Application.Activities;

public sealed class InMemoryActivitySessionStateStore : IActivitySessionStateStore
{
    private readonly object _sync = new();
    private ActivitySessionState _state = ActivitySessionState.Empty;

    public ActivitySessionState Current
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public ActivitySessionState Update(ActivityKind activity, string userRequest, string schemaName, string summary)
    {
        lock (_sync)
        {
            _state = new ActivitySessionState(
                activity,
                userRequest,
                schemaName,
                summary,
                LastBuildPassed: false,
                LastUnitTestsPassed: false,
                LastIntegrationTestsPassed: false,
                DateTimeOffset.UtcNow);
            return _state;
        }
    }
}
