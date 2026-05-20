using System;
using System.IO;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ChatConsoleStatusIndicatorTests
{
    [Fact]
    public void Status_Window_Command_Builder_Should_Build_A_Chat_Status_Command()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var command = ChatConsoleStatusWindowCommandBuilder.BuildCommandLine(
            @"C:\tools\McpServer.AgentRouter.Tools.dll",
            @"C:\temp\chat-status.lock");

        Assert.Contains("dotnet", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chat-status", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--status-lock", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\temp\chat-status.lock", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_Window_Indicator_Should_Write_And_Clear_State_File()
    {
        using var tempDirectory = new TempDirectory();
        var statePath = Path.Combine(tempDirectory.DirectoryPath, "chat-status.txt");

        var indicator = new StatusWindowChatConsoleStatusIndicator(statePath, "Waiting on GPU...");

        Assert.True(File.Exists(statePath));
        Assert.Equal("Waiting on GPU...", File.ReadAllText(statePath));

        indicator.Stop();
        Assert.Equal(string.Empty, File.ReadAllText(statePath));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "mcpserver-status-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
