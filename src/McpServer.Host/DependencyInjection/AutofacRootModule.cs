using Autofac;
using McpServer.Host.Configuration;
using Serilog;

namespace McpServer.Host.DependencyInjection;

public sealed class AutofacRootModule : Module
{
    private readonly IConfiguration _configuration;

    public AutofacRootModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void Load(ContainerBuilder builder)
    {
        var options = _configuration.GetSection(McpServerOptions.SectionName).Get<McpServerOptions>() ?? new McpServerOptions();
        var workspaceResolution = WorkspacePathResolver.ResolveWorkspaceRoot(options.Workspace, AppContext.BaseDirectory);
        var workspace = workspaceResolution.WorkspaceRoot;
        var allowedRoots = WorkspacePathResolver.BuildAllowedWorkspaceRoots(
            workspace,
            options.Workspace.AllowedRoots,
            options.Workspace.AdditionalAllowedRoots,
            AppContext.BaseDirectory);

        Directory.CreateDirectory(workspace);

        if (workspaceResolution.UsedApplicationDefault)
        {
            Log.Information(
                "No explicit MCP workspace root was configured. Created or reused default MCP workspace {WorkspaceRoot}",
                workspace);
        }

        Log.Information(
            "Resolved MCP workspace root {WorkspaceRoot} from {WorkspaceSource}. Configured value: {ConfiguredRootPath}. Allowed roots: {AllowedRoots}",
            workspace,
            workspaceResolution.Source,
            workspaceResolution.ConfiguredRootPath,
            allowedRoots);

        builder.RegisterInstance(options).AsSelf().SingleInstance();
        foreach (var module in BuildModules(options, workspace, allowedRoots, AppContext.BaseDirectory))
        {
            builder.RegisterModule(module);
        }
    }

    private static IEnumerable<Module> BuildModules(
        McpServerOptions options,
        string workspace,
        string[] allowedRoots,
        string baseDirectory)
    {
        yield return new RuntimeModule();
        yield return new WorkspaceStateModule(workspace, allowedRoots);
        yield return new WorkspaceToolModule();
        yield return new FileServiceModule();
        yield return new FileToolModule();
        yield return new FileResourceModule();
        yield return new ActivityServiceModule();
        yield return new ActivityToolModule();
        yield return new PromptModule();

        if (options.Shell.Enabled)
        {
            yield return new ShellFeatureModule(options.Shell);
        }

        if (options.Ollama.Enabled)
        {
            yield return new OllamaFeatureModule(options.Ollama);
        }

        if (options.WebAccess.Enabled)
        {
            yield return new WebFeatureModule(options.WebAccess);
        }

        if (options.Ssh.Enabled)
        {
            yield return new SshFeatureModule(options.Ssh, baseDirectory);
        }
    }
}
