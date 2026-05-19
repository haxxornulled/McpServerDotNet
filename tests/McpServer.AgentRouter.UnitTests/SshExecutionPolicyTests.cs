using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Application.Ssh;
using McpServer.AgentRouter.Domain.Ssh;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class SshExecutionPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_AllowsCommandForConfiguredNamedProfile()
    {
        var policy = CreatePolicy();

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "pwd",
            Arguments = [],
            WorkingDirectory = "/tmp"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.True(decision.Allowed);
            Assert.Equal("allowed", decision.Decision);
            Assert.Equal("dev", decision.ProfileName);
            Assert.Equal("127.0.0.1", decision.Host);
            Assert.Equal(22, decision.Port);
            Assert.Equal("tester", decision.Username);
            Assert.Equal("pwd", decision.ResolvedCommand);
            Assert.Equal("/tmp", decision.WorkingDirectory);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesUnknownProfile()
    {
        var policy = CreatePolicy();

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "prod",
            Command = "pwd"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("Unknown SSH profile", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesCommandOutsideProfileAllowlist()
    {
        var policy = CreatePolicy();

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "systemctl"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("not allowed", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesRemoteWorkingDirectoryOutsideAllowedPrefixes()
    {
        var policy = CreatePolicy();

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "pwd",
            WorkingDirectory = "/etc"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("working directory", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesInlineShellCommands_ByDefault()
    {
        var policy = CreatePolicy(allowedCommands: ["bash"]);

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "bash",
            Arguments = ["-c", "echo unsafe"]
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("inline command", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task EvaluateAsync_AllowsAnyCommand_When_Profile_Opts_In()
    {
        var policy = CreatePolicy(allowAllCommands: true);

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "ps"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.True(decision.Allowed);
            Assert.Equal("ps", decision.ResolvedCommand);
        });
    }

    [Fact]
    public async Task EvaluateAsync_AllowsSudo_When_Profile_Opts_In()
    {
        var policy = CreatePolicy(allowedCommands: ["whoami"], allowSudoCommand: true);

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "sudo whoami",
            Arguments = [],
            WorkingDirectory = "/tmp"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.True(decision.Allowed);
            Assert.Equal("sudo", decision.ResolvedCommand);
            Assert.Equal(["whoami"], decision.ResolvedArguments);
        });
    }

    [Fact]
    public async Task EvaluateAsync_AllowsSudo_With_Inline_Arguments_When_Profile_Opts_In()
    {
        var policy = CreatePolicy(allowedCommands: ["whoami"], allowSudoCommand: true);

        var result = await policy.EvaluateAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "sudo apt update",
            Arguments = [],
            WorkingDirectory = "/tmp"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.True(decision.Allowed);
            Assert.Equal("sudo", decision.ResolvedCommand);
            Assert.Equal(["apt", "update"], decision.ResolvedArguments);
        });
    }

    private static SshExecutionPolicy CreatePolicy(
        IList<string>? allowedCommands = null,
        bool allowSudoCommand = false,
        bool allowAllCommands = false)
    {
        var profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["dev"] = new SshProfileDefinition
            {
                Host = "127.0.0.1",
                Port = 22,
                Username = "tester",
                PasswordVaultItemName = "dev",
                WorkingDirectory = "/tmp",
                AllowedCommands = allowedCommands is null ? ["pwd", "whoami"] : allowedCommands.ToArray(),
                DeniedCommands = ["rm"],
                AllowedRemotePathPrefixes = ["/tmp"],
                AllowSudoCommand = allowSudoCommand,
                AllowAllCommands = allowAllCommands
            }
        };

        return new SshExecutionPolicy(
            TestRuntimeSettings.Create(
                sshExecution: new SshExecutionRuntimeSettings
            {
                Enabled = true,
                RequireExplicitProfileAllowlist = true,
                TimeoutSeconds = 60,
                MaxOutputChars = 200000,
                AllowUnknownHostKeys = false,
                AllowShellInterpreterInlineCommands = false,
                AllowedCommands = new System.Collections.Generic.HashSet<string>(["pwd", "whoami"], StringComparer.OrdinalIgnoreCase),
                DeniedCommands = new System.Collections.Generic.HashSet<string>(["rm", "sudo"], StringComparer.OrdinalIgnoreCase),
                WriteTraceFiles = false,
                TraceRootPath = Path.Combine("workspace", "artifacts", "ssh-exec"),
                LoadRepoProfilesFile = false,
                RepoProfilesFilePath = string.Empty,
                LoadUserProfilesFile = false,
                UserProfilesFilePath = string.Empty,
                AllowInlineProfiles = false,
                VaultPath = Path.Combine("workspace", "artifacts", "ssh-vault.json"),
                VaultKeyPath = Path.Combine("workspace", "artifacts", "ssh-vault.key"),
                Profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            }),
            new InMemorySshProfileStore(profiles));
    }

    private sealed class InMemorySshProfileStore : ISshProfileStore
    {
        private readonly IDictionary<string, SshProfileDefinition> _profiles;

        public InMemorySshProfileStore(IDictionary<string, SshProfileDefinition> profiles)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        }

        public ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return new ValueTask<Fin<SshProfileCatalog>>(Fin<SshProfileCatalog>.Succ(new SshProfileCatalog
            {
                Profiles = new Dictionary<string, SshProfileDefinition>(_profiles, StringComparer.OrdinalIgnoreCase),
                Sources =
                [
                    new SshProfileSourceStatus
                    {
                        SourceName = "unit-test",
                        Path = "memory",
                        Enabled = true,
                        Exists = true,
                        ProfileCount = _profiles.Count
                    }
                ]
            }));
        }
    }
}
