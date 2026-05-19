using Microsoft.Extensions.DependencyInjection;
using McpServer.Infrastructure.Ssh;
using Serilog;

namespace McpServer.SshVaultCli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SshProfileManager>();

        using var serviceProvider = services.BuildServiceProvider();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            return await VaultCli.RunAsync(args, serviceProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SSH vault CLI failed.");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
