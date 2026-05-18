using System.Text.Json;
using McpServer.IntegrationTests.Infrastructure;
using Xunit;

namespace McpServer.IntegrationTests.Protocol;

public sealed class StdioLifecycleIntegrationTests
{
    private static string HostProjectPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "McpServer.Host", "McpServer.Host.csproj"));


    [Fact]
    public async Task ContentLength_Framed_Initialize_Initialized_And_ToolsList_Should_Work_End_To_End()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new FramedJsonRpcTestClient(server.Input.BaseStream, server.Output.BaseStream);

        var initializeResponse = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "framed-stdio-test", version = "1.0.0" }
            }
        });

        Assert.NotNull(initializeResponse);
        Assert.False(initializeResponse!.RootElement.TryGetProperty("error", out _));
        Assert.Equal("2025-03-26", initializeResponse.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

        await client.SendNotificationAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });

        var toolsResponse = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        });

        Assert.NotNull(toolsResponse);
        Assert.False(toolsResponse!.RootElement.TryGetProperty("error", out _));

        var tools = toolsResponse.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .ToArray();

        Assert.NotEmpty(tools);
        Assert.DoesNotContain(
            server.StandardErrorLines,
            static line => line.Contains("Failed to deserialize JSON-RPC request line", StringComparison.Ordinal));
        Assert.DoesNotContain(
            server.StandardErrorLines,
            static line => line.Contains("Failed to deserialize JSON-RPC request message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Initialize_Response_Should_Match_Typed_Shape()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new JsonRpcTestClient(server.Input, server.Output);

        var response = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "xunit-test", version = "1.0.0" }
            }
        });

        Assert.NotNull(response);
    }

    [Fact]
    public async Task Initialize_Should_Fall_Back_To_Compatible_Server_Version_For_Unknown_Client_Request()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new JsonRpcTestClient(server.Input, server.Output);

        var response = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2026-01-01",
                capabilities = new { },
                clientInfo = new { name = "lmstudio-test", version = "1.0.0" }
            }
        });

        Assert.NotNull(response);
        Assert.Equal("2025-03-26", response!.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Ping_Should_Return_Empty_Result()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new JsonRpcTestClient(server.Input, server.Output);

        var response = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 99,
            method = "ping"
        });

        Assert.NotNull(response);
        Assert.False(response!.RootElement.TryGetProperty("error", out _));
        Assert.Equal(JsonValueKind.Object, response.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Host_Should_Write_Routine_Startup_Logs_To_Stderr_And_Keep_Stdout_For_JsonRpc()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new JsonRpcTestClient(server.Input, server.Output);

        var response = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "lmstudio-test", version = "1.0.0" }
            }
        });

        Assert.NotNull(response);
        Assert.False(response!.RootElement.TryGetProperty("error", out _));

        await client.SendNotificationAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });

        await Task.Delay(500);

        var stderrLines = server.StandardErrorLines;
        Assert.Contains(stderrLines, static line => line.Contains("MCP server started (stdio); awaiting JSON-RPC messages", StringComparison.Ordinal));
        Assert.DoesNotContain(stderrLines, static line => line.Contains(" FTL]", StringComparison.Ordinal) || line.Contains(" ERR]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_Should_Work_When_Started_From_An_Unrelated_Working_Directory()
    {
        var launchDirectory = Path.Combine(Path.GetTempPath(), "mcpserver-lmstudio-launch", Guid.NewGuid().ToString("N"));
        var defaultWorkspaceRoot = Path.Combine(Path.GetTempPath(), "mcpserver-default-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(launchDirectory);

        StdioTestServerProcess? server = null;

        try
        {
            server = await StdioTestServerProcess.StartAsync(
                HostProjectPath,
                launchDirectory,
                new Dictionary<string, string?>
                {
                    ["MCPSERVER_DEFAULT_WORKSPACE_ROOT"] = defaultWorkspaceRoot
                });
            var client = new JsonRpcTestClient(server.Input, server.Output);

            _ = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new { name = "lmstudio-test", version = "1.0.0" }
                }
            });

            await client.SendNotificationAsync(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });

            var writeResponse = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "fs.write_text",
                    arguments = new
                    {
                        path = "lmstudio-smoke.txt",
                        content = "lmstudio cwd ok",
                        overwrite = true
                    }
                }
            });

            Assert.NotNull(writeResponse);
            Assert.False(writeResponse!.RootElement.TryGetProperty("error", out _));

            var expectedWorkspaceFile = Path.Combine(defaultWorkspaceRoot, "lmstudio-smoke.txt");

            Assert.True(File.Exists(expectedWorkspaceFile));
        }
        finally
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            await DeleteDirectoryWithRetryAsync(launchDirectory);
            await DeleteDirectoryWithRetryAsync(defaultWorkspaceRoot);
        }
    }

    [Fact]
    public async Task File_Tool_And_Resource_Flow_Should_Work_End_To_End()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new JsonRpcTestClient(server.Input, server.Output);

        _ = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "xunit-test", version = "1.0.0" }
            }
        });

        await client.SendNotificationAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });

        var writeResponse = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = "fs.write_text",
                arguments = new
                {
                    path = "smoke.txt",
                    content = "smoke test ok",
                    overwrite = true
                }
            }
        });

        Assert.NotNull(writeResponse);
        Assert.False(writeResponse!.RootElement.TryGetProperty("error", out _));

        var readResponse = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "resources/read",
            @params = new
            {
                uri = "file:///workspace/smoke.txt"
            }
        });

        Assert.NotNull(readResponse);
        Assert.False(readResponse!.RootElement.TryGetProperty("error", out _));

        var contents = readResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents");

        var array = contents.EnumerateArray().ToArray();
        Assert.Single(array);
        var content = array[0];
        Assert.Equal("smoke test ok", content.GetProperty("text").GetString());

        var treeResponse = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "resources/read",
            @params = new
            {
                uri = "tree:///project"
            }
        });

        Assert.NotNull(treeResponse);
        Assert.False(treeResponse!.RootElement.TryGetProperty("error", out _));

        var treeContents = treeResponse.RootElement
            .GetProperty("result")
            .GetProperty("contents");

        var treeArray = treeContents.EnumerateArray().ToArray();
        Assert.Single(treeArray);
        var treeText = treeArray[0].GetProperty("text").GetString();
        Assert.NotNull(treeText);
        Assert.Contains("\"smoke.txt\"", treeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_Set_Root_Should_Update_File_Tool_Workspace_End_To_End()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "mcpserver-stdio-set-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            await using var server = await StdioTestServerProcess.StartAsync(
                HostProjectPath,
                workingDirectory: null,
                environmentVariables: new Dictionary<string, string?>
                {
                    ["McpServer__Workspace__AdditionalAllowedRoots__0"] = workspaceRoot
                });
            var client = new JsonRpcTestClient(server.Input, server.Output);

            _ = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new { name = "xunit-test", version = "1.0.0" }
                }
            });

            await client.SendNotificationAsync(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });

            var setRootResponse = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "workspace.set_root",
                    arguments = new
                    {
                        path = workspaceRoot
                    }
                }
            });

            Assert.NotNull(setRootResponse);
            Assert.False(setRootResponse!.RootElement.TryGetProperty("error", out _));

            var writeResponse = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "fs.write_text",
                    arguments = new
                    {
                        path = "after-root-switch.txt",
                        content = "new root ok",
                        overwrite = true
                    }
                }
            });

            Assert.NotNull(writeResponse);
            Assert.False(writeResponse!.RootElement.TryGetProperty("error", out _));
            Assert.Equal("new root ok", await File.ReadAllTextAsync(Path.Combine(workspaceRoot, "after-root-switch.txt")));

            var listResponse = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new
                {
                    name = "fs.list_directory",
                    arguments = new
                    {
                        path = "workspace"
                    }
                }
            });

            Assert.NotNull(listResponse);
            Assert.False(listResponse!.RootElement.TryGetProperty("error", out _));

            var listText = listResponse.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            Assert.Contains("after-root-switch.txt", listText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Client_Roots_Should_Not_Expand_Configured_Allowed_Roots()
    {
        var allowedRoot = Path.Combine(Path.GetTempPath(), "mcpserver-allowed-root", Guid.NewGuid().ToString("N"));
        var rogueRoot = Path.Combine(Path.GetTempPath(), "mcpserver-rogue-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(rogueRoot);

        try
        {
            await using var server = await StdioTestServerProcess.StartAsync(
                HostProjectPath,
                workingDirectory: null,
                environmentVariables: new Dictionary<string, string?>
                {
                    ["McpServer__Workspace__RootPath"] = allowedRoot
                });
            var client = new JsonRpcTestClient(server.Input, server.Output);

            _ = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new
                    {
                        roots = new
                        {
                            listChanged = true
                        }
                    },
                    clientInfo = new { name = "xunit-test", version = "1.0.0" }
                }
            });

            await client.SendNotificationAsync(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });

            using var rootsRequest = await client.ReadMessageAsync();
            Assert.NotNull(rootsRequest);
            Assert.Equal("roots/list", rootsRequest!.RootElement.GetProperty("method").GetString());

            await client.SendSuccessResponseAsync(
                rootsRequest.RootElement.GetProperty("id"),
                new
                {
                    roots = new[]
                    {
                        new
                        {
                            uri = new Uri(rogueRoot).AbsoluteUri,
                            name = "rogue"
                        }
                    }
                });

            var setRootResponse = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "workspace.set_root",
                    arguments = new
                    {
                        path = rogueRoot
                    }
                }
            });

            Assert.NotNull(setRootResponse);
            Assert.False(setRootResponse!.RootElement.TryGetProperty("error", out _));

            var setRootResult = setRootResponse.RootElement.GetProperty("result");
            Assert.True(setRootResult.GetProperty("isError").GetBoolean());
            Assert.Contains(
                "outside allowed roots",
                setRootResult.GetProperty("content")[0].GetProperty("text").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var statusResponse = await client.SendRequestAsync(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "workspace.status",
                    arguments = new { }
                }
            });

            Assert.NotNull(statusResponse);
            Assert.False(statusResponse!.RootElement.TryGetProperty("error", out _));

            var status = statusResponse.RootElement
                .GetProperty("result")
                .GetProperty("structuredContent");

            Assert.Equal(allowedRoot, status.GetProperty("workspaceRoot").GetString());
            Assert.Equal(allowedRoot, status.GetProperty("projectRoot").GetString());

            var allowedRoots = status.GetProperty("allowedRoots").EnumerateArray().Select(static x => x.GetString()).ToArray();
            Assert.Contains(allowedRoot, allowedRoots);
            Assert.DoesNotContain(rogueRoot, allowedRoots);
        }
        finally
        {
            if (Directory.Exists(allowedRoot))
            {
                Directory.Delete(allowedRoot, recursive: true);
            }

            if (Directory.Exists(rogueRoot))
            {
                Directory.Delete(rogueRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Shell_Exec_Tool_Should_Run_Dotnet_Version_In_Workspace()
    {
        await using var server = await StdioTestServerProcess.StartAsync(
            HostProjectPath,
            workingDirectory: null,
            environmentVariables: new Dictionary<string, string?>
            {
                ["McpServer__Shell__Enabled"] = "true",
                ["McpServer__Shell__AllowedCommands__0"] = "dotnet"
            });
        var client = new JsonRpcTestClient(server.Input, server.Output);

        _ = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "xunit-test", version = "1.0.0" }
            }
        });

        await client.SendNotificationAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });

        var execResponse = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new
            {
                name = "shell.exec",
                arguments = new
                {
                    command = "dotnet",
                    args = new[] { "--version" },
                    timeoutSeconds = 30
                }
            }
        });

        Assert.NotNull(execResponse);
        Assert.False(execResponse!.RootElement.TryGetProperty("error", out _));

        var result = execResponse.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());

        var structuredContent = result.GetProperty("structuredContent");
        Assert.Equal(0, structuredContent.GetProperty("exitCode").GetInt32());
        Assert.False(structuredContent.GetProperty("timedOut").GetBoolean());

        var stdout = structuredContent.GetProperty("standardOutput").GetString();
        Assert.False(string.IsNullOrWhiteSpace(stdout));
    }

    [Fact]
    public async Task Initialize_Response_Should_Omit_Null_Error_Field()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);
        var client = new JsonRpcTestClient(server.Input, server.Output);

        var response = await client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "shape-test", version = "1.0.0" }
            }
        });

        Assert.NotNull(response);
        Assert.False(response!.RootElement.TryGetProperty("error", out _));
        Assert.True(response.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Host_Should_Exit_When_Stdin_Closes()
    {
        await using var server = await StdioTestServerProcess.StartAsync(HostProjectPath);

        server.CloseInput();

        var exited = await server.WaitForExitAsync(TimeSpan.FromSeconds(5));

        Assert.True(exited);
        Assert.False(server.IsAlive);
    }
    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        const int maxAttempts = 10;
        var delay = TimeSpan.FromMilliseconds(100);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

}
