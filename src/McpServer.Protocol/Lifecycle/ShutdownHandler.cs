using LanguageExt;
using McpServer.Protocol.Session;

namespace McpServer.Protocol.Lifecycle;

public sealed class ShutdownHandler
{
    public Fin<Unit> Handle(McpSession session) => session.RequestShutdown();
}
