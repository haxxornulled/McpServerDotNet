using LanguageExt;
using McpServer.Application.Mcp.Tools;

namespace McpServer.Application.Execution;

public interface IShellExecutionPolicy
{
    Fin<Unit> Validate(ShellExecRequest request, bool requiresShellFallback) =>
        Validate(request, requiresShellFallback, requiresWindowsCompatibilityShell: false);

    Fin<Unit> Validate(
        ShellExecRequest request,
        bool requiresShellFallback,
        bool requiresWindowsCompatibilityShell);
}
