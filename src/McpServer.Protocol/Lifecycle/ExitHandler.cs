using Microsoft.Extensions.Logging;
using McpServer.Protocol.Session;

namespace McpServer.Protocol.Lifecycle;

public sealed class ExitHandler
{
    private readonly ILogger<ExitHandler> _logger;

    public ExitHandler(ILogger<ExitHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Handle(McpSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.IsShutdownRequested)
        {
            _logger.LogInformation("Received exit after shutdown request");
            return true;
        }

        _logger.LogWarning("Received exit without prior shutdown request");
        return true;
    }
}
