using Serilog;

namespace McpServer.SshVaultCli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            return await VaultCli.RunAsync(args).ConfigureAwait(false);
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
