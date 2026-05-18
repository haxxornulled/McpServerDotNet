using LanguageExt;
namespace McpServer.Application.Activities;

public interface IActivityProfileRegistry
{
    IReadOnlyList<ActivityProfileDto> ListProfiles();
    Fin<ActivityProfileDto> GetProfile(ActivityKind activity);
    Fin<ActivityKind> ParseActivity(string? activity);
}
