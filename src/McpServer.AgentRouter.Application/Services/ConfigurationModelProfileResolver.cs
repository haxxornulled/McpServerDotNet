using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Services;

/// <summary>
/// Resolves model profiles from the configured runtime settings.
/// </summary>
public sealed class ConfigurationModelProfileResolver : IModelProfileResolver
{
    private readonly AgentRouterRuntimeSettings _settings;

    /// <summary>
    /// Initializes a new configuration-backed profile resolver.
    /// </summary>
    public ConfigurationModelProfileResolver(AgentRouterRuntimeSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Resolves a profile by name.
    /// </summary>
    public ValueTask<Fin<ModelProfile>> ResolveAsync(string? profileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedName = string.IsNullOrWhiteSpace(profileName)
            ? _settings.DefaultProfile
            : profileName.Trim();

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            return ValueTask.FromResult<Fin<ModelProfile>>(
                Error.New("Model profile name is required and AgentRouter:DefaultProfile is not configured."));
        }

        if (!_settings.ModelProfiles.TryGetValue(resolvedName, out var profile))
        {
            return ValueTask.FromResult<Fin<ModelProfile>>(
                Error.New($"Unknown model profile '{resolvedName}'."));
        }

        return ValueTask.FromResult<Fin<ModelProfile>>(Fin<ModelProfile>.Succ(profile));
    }

    /// <summary>
    /// Lists all configured profiles.
    /// </summary>
    public IReadOnlyList<ModelProfile> ListProfiles()
    {
        if (_settings.ModelProfiles.Count == 0)
        {
            return global::System.Array.AsReadOnly(global::System.Array.Empty<ModelProfile>());
        }

        return global::System.Array.AsReadOnly(
            _settings.ModelProfiles.Values
                .OrderBy(profile => IsDefaultProfile(profile.Name) ? 0 : 1)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private bool IsDefaultProfile(string profileName)
    {
        return !string.IsNullOrWhiteSpace(_settings.DefaultProfile)
               && string.Equals(
                   profileName,
                   _settings.DefaultProfile,
                   StringComparison.OrdinalIgnoreCase);
    }
}
