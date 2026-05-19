using Autofac;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Activities;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Execution;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Mcp.Prompts;
using McpServer.Application.Mcp.Resources;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Tools.Inference;
using McpServer.Application.Files;
using McpServer.Infrastructure.Execution;
using McpServer.Host.Configuration;
using McpServer.Infrastructure.Files;
using McpServer.Infrastructure.Ollama;
using McpServer.Infrastructure.Ssh;
using McpServer.Infrastructure.Web;
using McpServer.Domain.Workspace;
using McpServer.Protocol.Lifecycle;
using McpServer.Protocol.Routing;
using McpServer.Protocol.Session;
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

        builder.RegisterType<McpSession>().SingleInstance();
        builder.RegisterType<CapabilityProvider>().SingleInstance();
        builder.RegisterType<InitializeHandler>().SingleInstance();
        builder.RegisterType<ShutdownHandler>().SingleInstance();
        builder.RegisterType<ExitHandler>().SingleInstance();
        builder.RegisterType<ToolCallRouter>().SingleInstance();
        builder.RegisterType<ResourceReadRouter>().SingleInstance();
        builder.RegisterType<PromptRouter>().SingleInstance();

        builder.RegisterType<ActivityProfileRegistry>()
            .As<IActivityProfileRegistry>()
            .SingleInstance();

        builder.RegisterType<RuleFirstActivityRouter>()
            .As<IActivityRouter>()
            .SingleInstance();

        builder.RegisterType<StructuredOutputSchemaRegistry>()
            .As<IStructuredOutputSchemaRegistry>()
            .SingleInstance();

        builder.RegisterType<ActivityContextBuilder>()
            .As<IActivityContextBuilder>()
            .SingleInstance();

        builder.RegisterType<InMemoryActivitySessionStateStore>()
            .As<IActivitySessionStateStore>()
            .SingleInstance();

        builder.RegisterType<FileMutationLockProvider>()
            .As<IFileMutationLockProvider>()
            .SingleInstance();

        builder.RegisterType<WorkspaceChangeFeed>()
            .As<IWorkspaceChangeFeed>()
            .SingleInstance();

        builder.RegisterType<WorkspaceMutationService>()
            .As<McpServer.Domain.Workspace.IWorkspaceMutationService>()
            .SingleInstance();

        builder.Register(ctx =>
            {
                var watcher = new WorkspaceFileWatcher(ctx.Resolve<WorkspacePathState>(), ctx.Resolve<IWorkspaceChangeFeed>());
                watcher.SetProjectRoot(workspace);
                return watcher;
            })
            .As<IWorkspaceFileWatcher>()
            .SingleInstance();

        builder.Register(_ => new WorkspacePathState(allowedRoots))
            .AsSelf()
            .SingleInstance();

        builder.Register(ctx => new ResourcePathTranslator(ctx.Resolve<WorkspacePathState>()))
            .AsSelf()
            .As<IResourcePathTranslator>()
            .SingleInstance();

        builder.Register(ctx => new PathPolicy(ctx.Resolve<WorkspacePathState>()))
            .AsSelf()
            .As<IPathPolicy>()
            .SingleInstance();

        builder.RegisterType<DestructiveFileOperationPolicy>()
            .As<IDestructiveFileOperationPolicy>()
            .SingleInstance();

        builder.RegisterType<FileSystemService>()
            .As<IFileSystemService>()
            .SingleInstance();

        builder.RegisterType<McpServer.Infrastructure.Execution.ProcessExecutionService>()
            .As<IProcessExecutionService>()
            .SingleInstance();


        RegisterTool<FsWriteTextToolHandler>(builder);
        RegisterTool<FsAppendTextToolHandler>(builder);
        RegisterTool<FsReadFileToolHandler>(builder);
        RegisterTool<FsReadTextToolHandler>(builder);
        RegisterTool<FsGetMetadataToolHandler>(builder);
        RegisterTool<FsListDirectoryToolHandler>(builder);
        RegisterTool<FsCreateDirectoryToolHandler>(builder);
        RegisterTool<FsMovePathToolHandler>(builder);
        RegisterTool<FsCopyPathToolHandler>(builder);
        RegisterTool<FsDeletePathToolHandler>(builder);
        RegisterTool<WorkspaceSetRootToolHandler>(builder);
        RegisterTool<WorkspaceOpenToolHandler>(builder);
        RegisterTool<WorkspaceSelectFolderToolHandler>(builder);
        RegisterTool<WorkspaceStatusToolHandler>(builder);
        RegisterTool<WorkspaceInspectToolHandler>(builder);
        RegisterTool<ActivityRouteToolHandler>(builder);
        RegisterTool<ActivitySchemasListToolHandler>(builder);
        RegisterTool<ActivityContextPreviewToolHandler>(builder);
        RegisterTool<ActivityRunToolHandler>(builder);

        if (options.Shell.Enabled)
        {
            builder.RegisterInstance(new ShellExecutionPolicyOptions(
                    options.Shell.AllowShellFallback,
                    options.Shell.AllowedCommands ?? [],
                    options.Shell.DeniedCommands ?? ShellExecutionPolicyOptions.DefaultDeniedCommands,
                    Math.Max(1, options.Shell.MaxTimeoutSeconds),
                    Math.Max(256, options.Shell.MaxOutputChars)))
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<ShellExecutionPolicy>()
                .As<IShellExecutionPolicy>()
                .SingleInstance();

            RegisterTool<ShellExecToolHandler>(builder);
        }

        builder.RegisterType<FsFileTextResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<FsDirectoryResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<FsFileMetadataResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<WorkspaceTreeResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<WorkspaceChangesResourceHandler>().As<IResourceHandler>().SingleInstance();

        builder.RegisterType<SummarizeFilePromptHandler>().As<IPromptHandler>().SingleInstance();
        builder.RegisterType<ReviewDirectoryPromptHandler>().As<IPromptHandler>().SingleInstance();


        if (options.Ollama.Enabled)
        {
            builder.Register(_ => new OllamaInferenceOptions(
                    enabled: options.Ollama.Enabled,
                    baseUrl: options.Ollama.BaseUrl,
                    defaultModel: options.Ollama.DefaultModel,
                    allowedModels: options.Ollama.AllowedModels,
                    timeoutSeconds: options.Ollama.TimeoutSeconds,
                    maxTimeoutSeconds: options.Ollama.MaxTimeoutSeconds,
                    maxPromptChars: options.Ollama.MaxPromptChars,
                    maxOutputChars: options.Ollama.MaxOutputChars,
                    contextLength: options.Ollama.ContextLength,
                    numPredict: options.Ollama.NumPredict,
                    temperature: options.Ollama.Temperature,
                    allowNonLoopbackBaseUrl: options.Ollama.AllowNonLoopbackBaseUrl))
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<OllamaInferenceService>()
                .As<ILocalInferenceService>()
                .SingleInstance();

            RegisterTool<LocalInferenceStatusToolHandler>(builder);
            RegisterTool<LocalCompleteToolHandler>(builder);
            RegisterTool<LocalSummarizeToolHandler>(builder);
            RegisterTool<LocalCodeReviewToolHandler>(builder);
            RegisterTool<LocalPlanToolHandler>(builder);
        }

        if (options.WebAccess.Enabled)
        {
            builder.Register(ctx =>
                {
                    var allowedHosts = new System.Collections.Generic.HashSet<string>(
                        options.WebAccess.AllowedHosts.Where(static x => !string.IsNullOrWhiteSpace(x)),
                        StringComparer.OrdinalIgnoreCase);

                    return new WebPolicy(
                        allowedHosts,
                        options.WebAccess.AllowLocalLoopbackHosts,
                        options.WebAccess.SearchBaseUrl);
                })
                .As<IWebPolicy>()
                .SingleInstance();

            builder.RegisterType<DuckDuckGoHtmlSearchProvider>()
                .As<IWebSearchProvider>()
                .SingleInstance();

            builder.RegisterType<WebFetchService>().As<IWebFetchService>().SingleInstance();
            builder.RegisterType<WebSearchService>().As<IWebSearchService>().SingleInstance();
            builder.RegisterType<WebScrapeService>().As<IWebScrapeService>().SingleInstance();
            RegisterTool<WebFetchUrlToolHandler>(builder);
            RegisterTool<WebSearchToolHandler>(builder);
            RegisterTool<WebScrapeUrlToolHandler>(builder);
        }

        if (options.Ssh.Enabled)
        {
            builder.RegisterInstance(new SshCredentialVaultStore(
                    options.Ssh.VaultPath,
                    options.Ssh.VaultKeyPath,
                    AppContext.BaseDirectory))
                .AsSelf()
                .SingleInstance();

            var configuredProfiles = FileSystemSshProfileStore.LoadProfiles(
                AppContext.BaseDirectory,
                options.Ssh.LoadRepoProfilesFile,
                options.Ssh.RepoProfilesFilePath,
                options.Ssh.LoadUserProfilesFile,
                options.Ssh.UserProfilesFilePath,
                options.Ssh.AllowInlineProfiles,
                CreateConfiguredProfiles(options.Ssh.Profiles));

            if (configuredProfiles.Count > 0)
            {
                if (options.Ssh.UseTestBackend)
                {
                    builder.Register(ctx => new TestSshService(
                            configuredProfiles,
                            string.IsNullOrWhiteSpace(options.Ssh.TestBackendRootPath)
                                ? Path.Combine(Path.GetTempPath(), "mcpserver-ssh-test-backend")
                                : options.Ssh.TestBackendRootPath,
                            ctx.Resolve<ILogger<TestSshService>>()))
                        .As<ISshService>()
                        .SingleInstance();
                }
                else
                {
                    builder.Register(ctx => new SshService(
                            configuredProfiles,
                            AppContext.BaseDirectory,
                            ctx.Resolve<ILogger<SshService>>(),
                            ctx.Resolve<SshCredentialVaultStore>()))
                        .As<ISshService>()
                        .SingleInstance();
                }

                RegisterTool<SshExecuteToolHandler>(builder);
                RegisterTool<SshWriteTextToolHandler>(builder);
            }
        }
    }


    private static void RegisterTool<TToolHandler>(ContainerBuilder builder)
        where TToolHandler : class, IToolHandler
    {
        builder.RegisterType<TToolHandler>()
            .AsSelf()
            .As<IToolHandler>()
            .SingleInstance();
    }

    private static ConfiguredSshProfile[] CreateConfiguredProfiles(IEnumerable<SshProfileOptions> profiles) =>
        profiles
            .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(static profile => new ConfiguredSshProfile(
                profile.Name,
                profile.Host,
                profile.Port,
                profile.Username,
                profile.PrivateKeyPath,
                profile.PasswordVaultItemName,
                profile.PrivateKeyPassphraseVaultItemName,
                profile.WorkingDirectory,
                profile.HostKeySha256,
                profile.AcceptUnknownHostKey,
                profile.AllowedCommands,
                profile.DeniedCommands,
                profile.AllowedRemotePathPrefixes,
                profile.AllowSudoCommand))
            .ToArray();
}
