using System.Text.Json;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentRuns;
using McpServer.AgentRouter.Infrastructure.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class FileSystemAgentRunStoreTests
{
    [Fact]
    public async Task SaveAsync_WritesRunRequestResponseAndArtifactFiles()
    {
        var rootPath = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(rootPath);
            var run = CreateCompletedRun();
            var request = new AgentRunRequest
            {
                Model = "fast-local",
                Goal = "Persist this run.",
                Instructions = "Use durable storage."
            };

            await store.SaveAsync(run, request, CancellationToken.None);

            var runDirectory = Path.Combine(rootPath, run.Id);
            Assert.True(Directory.Exists(runDirectory));
            Assert.True(File.Exists(Path.Combine(runDirectory, "request.json")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "response.json")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "artifacts.json")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "plan.md")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "generation.md")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "trace.json")));

            var requestJson = await File.ReadAllTextAsync(Path.Combine(runDirectory, "request.json"));
            var responseJson = await File.ReadAllTextAsync(Path.Combine(runDirectory, "response.json"));

            Assert.Contains("\"max_tokens\"", requestJson);
            Assert.Contains("\"created_at\"", responseJson);
        }
        finally
        {
            DeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task GetAsync_ReadsRunSavedByPreviousStoreInstance()
    {
        var rootPath = CreateTemporaryDirectory();
        try
        {
            var firstStore = CreateStore(rootPath);
            var run = CreateCompletedRun();

            await firstStore.SaveAsync(run, new AgentRunRequest
            {
                Model = "fast-local",
                Goal = run.Goal
            }, CancellationToken.None);

            var secondStore = CreateStore(rootPath);
            var loaded = await secondStore.GetAsync(run.Id, CancellationToken.None);

            Assert.True(loaded.IsSucc);
            loaded.IfSucc(value =>
            {
                Assert.Equal(run.Id, value.Id);
                Assert.Equal(AgentRunStatusNames.Completed, value.Status);
                Assert.Equal("persisted result", value.Result);
                Assert.Contains(value.Artifacts, artifact => artifact.Type == "generation");
            });
        }
        finally
        {
            DeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task GetAsync_ReadsLegacySnakeCaseResponseJson()
    {
        var rootPath = CreateTemporaryDirectory();
        try
        {
            var runId = "run-" + Guid.NewGuid().ToString("N");
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var runDirectory = Path.Combine(rootPath, runId);
            Directory.CreateDirectory(runDirectory);

            var legacyRun = new Dictionary<string, object?>
            {
                ["id"] = runId,
                ["object"] = "agent.run",
                ["status"] = AgentRunStatusNames.Completed,
                ["model"] = "fast-local",
                ["goal"] = "Persist this run.",
                ["created_at"] = createdAt,
                ["updated_at"] = createdAt.AddMinutes(1),
                ["completed_at"] = createdAt.AddMinutes(1),
                ["result"] = "persisted result",
                ["error"] = null,
                ["artifacts"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "artifact-" + Guid.NewGuid().ToString("N"),
                        ["type"] = "generation",
                        ["name"] = "generation",
                        ["content"] = "persisted result",
                        ["created_at"] = createdAt
                    }
                }
            };

            await File.WriteAllTextAsync(
                    Path.Combine(runDirectory, "response.json"),
                    JsonSerializer.Serialize(legacyRun),
                    CancellationToken.None);

            var store = CreateStore(rootPath);
            var loaded = await store.GetAsync(runId, CancellationToken.None);

            Assert.True(loaded.IsSucc);
            loaded.IfSucc(value =>
            {
                Assert.Equal(runId, value.Id);
                Assert.Equal(AgentRunStatusNames.Completed, value.Status);
                Assert.Equal("persisted result", value.Result);
                Assert.Single(value.Artifacts);
                Assert.Equal("generation", value.Artifacts[0].Type);
            });
        }
        finally
        {
            DeleteDirectory(rootPath);
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../bad")]
    [InlineData("bad/path")]
    [InlineData("bad\\path")]
    public async Task GetAsync_ReturnsNotFound_ForUnsafeRunIds(string runId)
    {
        var rootPath = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(rootPath);

            var loaded = await store.GetAsync(runId, CancellationToken.None);

            Assert.True(loaded.IsFail);
        }
        finally
        {
            DeleteDirectory(rootPath);
        }
    }

    private static FileSystemAgentRunStore CreateStore(string rootPath)
    {
        var settings = TestRuntimeSettings.Create(
            runStorage: new AgentRunStorageRuntimeSettings
            {
                RootPath = rootPath,
                WriteArtifactFiles = true
            });

        return new FileSystemAgentRunStore(
            settings,
            new AgentRouterRuntimePathResolver(),
            NullLogger<FileSystemAgentRunStore>.Instance);
    }

    private static AgentRun CreateCompletedRun()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new AgentRun
        {
            Id = "run-" + Guid.NewGuid().ToString("N"),
            Status = AgentRunStatusNames.Completed,
            Model = "fast-local",
            Goal = "Persist this run.",
            CreatedAt = now.AddSeconds(-1),
            UpdatedAt = now,
            CompletedAt = now,
            Result = "persisted result"
        };

        run.Artifacts.Add(new AgentRunArtifact
        {
            Id = "artifact-" + Guid.NewGuid().ToString("N"),
            Type = "plan",
            Name = "plan",
            Content = "persisted plan",
            CreatedAt = now
        });

        run.Artifacts.Add(new AgentRunArtifact
        {
            Id = "artifact-" + Guid.NewGuid().ToString("N"),
            Type = "generation",
            Name = "generation",
            Content = "persisted result",
            CreatedAt = now
        });

        run.Artifacts.Add(new AgentRunArtifact
        {
            Id = "artifact-" + Guid.NewGuid().ToString("N"),
            Type = "trace",
            Name = "trace",
            Content = "trace content",
            CreatedAt = now
        });

        return run;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-router-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
