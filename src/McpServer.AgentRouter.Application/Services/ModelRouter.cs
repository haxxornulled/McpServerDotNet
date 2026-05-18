using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Inference;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.Services;

public sealed class ModelRouter : IModelRouter
{
    private readonly IModelProfileResolver _profileResolver;
    private readonly IReadOnlyList<IChatModelClient> _clients;
    private readonly ILogger<ModelRouter> _logger;

    public ModelRouter(
        IModelProfileResolver profileResolver,
        IEnumerable<IChatModelClient> clients,
        ILogger<ModelRouter> logger)
    {
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _clients = (clients ?? throw new ArgumentNullException(nameof(clients))).ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<ModelTurnResult>> CompleteAsync(
        ModelInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0)
        {
            return Error.New("At least one chat message is required.");
        }

        var profileResult = await _profileResolver.ResolveAsync(request.ModelProfileName, cancellationToken)
            .ConfigureAwait(false);

        if (profileResult.IsFail)
        {
            return profileResult.Match<Fin<ModelTurnResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected profile resolution success."),
                Fail: error => error);
        }

        var profile = profileResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected profile resolution failure."));

        var client = _clients.FirstOrDefault(item =>
            string.Equals(item.ProviderName, profile.Provider, StringComparison.OrdinalIgnoreCase));

        if (client is null)
        {
            _logger.LogWarning(
                "No chat model client is registered for provider {Provider}.",
                profile.Provider);

            return Error.New($"No chat model client is registered for provider '{profile.Provider}'.");
        }

        _logger.LogDebug(
            "Routing model profile {ProfileName} to provider {Provider} model {Model}.",
            profile.Name,
            profile.Provider,
            profile.Model);

        return await client.CompleteAsync(request, profile, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<Fin<ModelTurnStream>> StreamAsync(
        ModelInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0)
        {
            return Error.New("At least one chat message is required.");
        }

        var profileResult = await _profileResolver.ResolveAsync(request.ModelProfileName, cancellationToken)
            .ConfigureAwait(false);

        if (profileResult.IsFail)
        {
            return profileResult.Match<Fin<ModelTurnStream>>(
                Succ: _ => throw new InvalidOperationException("Unexpected profile resolution success."),
                Fail: error => error);
        }

        var profile = profileResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected profile resolution failure."));

        var client = _clients.FirstOrDefault(item =>
            string.Equals(item.ProviderName, profile.Provider, StringComparison.OrdinalIgnoreCase));

        if (client is null)
        {
            _logger.LogWarning(
                "No chat model client is registered for provider {Provider}.",
                profile.Provider);

            return Error.New($"No chat model client is registered for provider '{profile.Provider}'.");
        }

        _logger.LogDebug(
            "Routing streaming model profile {ProfileName} to provider {Provider} model {Model}.",
            profile.Name,
            profile.Provider,
            profile.Model);

        return await client.StreamAsync(request, profile, cancellationToken)
            .ConfigureAwait(false);
    }
}
