using System;
using System.IO;
using McpServer.SshVaultCli;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class SshVaultCliJsonOutputTests
{
    [Fact]
    public void List_Command_Should_Emit_Json_When_Requested()
    {
        using var tempRoot = new TempDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = VaultCli.Run([
                "list",
                "--vault-path",
                System.IO.Path.Combine(tempRoot.DirectoryPath, "config", "mcpserver", "ssh-vault.local.json"),
                "--vault-key-path",
                System.IO.Path.Combine(tempRoot.DirectoryPath, "ssh-vault.key"),
                "--base-directory",
                tempRoot.DirectoryPath,
                "--output",
                "json"
            ]);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"command\": \"list\"", output.ToString());
            Assert.Contains("\"entries\": []", output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Profile_List_Should_Emit_Json_When_Requested()
    {
        using var tempRoot = new TempDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = VaultCli.Run([
                "profile",
                "upsert",
                "dev",
                "--host",
                "localhost",
                "--username",
                "root",
                "--allow-all-commands",
                "true",
                "--base-directory",
                tempRoot.DirectoryPath
            ]);

            Assert.Equal(0, exitCode);
            output.GetStringBuilder().Clear();

            exitCode = VaultCli.Run([
                "profile",
                "list",
                "--base-directory",
                tempRoot.DirectoryPath,
                "--output",
                "json"
            ]);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"command\": \"profile list\"", output.ToString());
            Assert.Contains("\"profiles\": [", output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcpserver-vault-json-" + Guid.NewGuid().ToString("N"));
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
