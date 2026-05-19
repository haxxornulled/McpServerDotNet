using LanguageExt;
using McpServer.Application.Mcp.Tools;

namespace McpServer.Application.Execution;

public interface IShellExecutionPolicy
{
    Fin<Unit> Validate(ShellExecRequest request, bool requiresShellFallback);
}
