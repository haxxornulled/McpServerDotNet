using McpServer.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class SerilogBootstrapTests
{
    [Fact]
    public void Configure_Should_Create_Logger_With_File_And_Stderr_Sinks_Without_Invalid_FileSink_Settings()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "mcpserver-serilog-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:Logging:Directory"] = logDirectory,
                    ["McpServer:Logging:AsyncBufferSize"] = "128",
                    ["McpServer:Logging:RetainedFileCountLimit"] = "2",
                    ["McpServer:Logging:FileSizeLimitBytes"] = "1048576"
                })
                .Build();

            var loggerConfiguration = new LoggerConfiguration();
            var exception = Record.Exception(() => SerilogBootstrap.Configure(loggerConfiguration, configuration));

            Assert.Null(exception);

            var logger = loggerConfiguration.CreateLogger();
            try
            {
                logger.Information("Serilog bootstrap regression test message");
            }
            finally
            {
                (logger as IDisposable)?.Dispose();
            }

            Assert.True(Directory.Exists(logDirectory));
        }
        finally
        {
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }
}
