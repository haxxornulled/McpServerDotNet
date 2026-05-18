using Xunit;

namespace McpServer.IntegrationTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvironmentConditionalFactAttribute : FactAttribute
{
    public EnvironmentConditionalFactAttribute(string environmentVariableName, string expectedValue = "true")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);

        var actual = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.Equals(actual, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Set {environmentVariableName}={expectedValue} to enable this opt-in integration test.";
        }
    }
}
