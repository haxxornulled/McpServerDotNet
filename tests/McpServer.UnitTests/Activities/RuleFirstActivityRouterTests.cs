using McpServer.Application.Activities;
using Xunit;

namespace McpServer.UnitTests.Activities;

public sealed class RuleFirstActivityRouterTests
{
    [Fact]
    public async Task RouteAsync_Should_Classify_Lyrics_Request_As_Explain_With_Default_Confidence()
    {
        var router = new RuleFirstActivityRouter(new ActivityProfileRegistry());

        var result = await router.RouteAsync("Give me the lyrics for Closer to the Heart by Rush.", CancellationToken.None);

        Assert.Equal(ActivityKind.Explain, result.Activity);
        Assert.Equal(0.60, result.Confidence, precision: 2);
        Assert.Equal("No stronger activity rule matched; defaulting to explanation.", result.Reason);
        Assert.False(result.RequiresWorkspace);
        Assert.False(result.RequiresShell);
        Assert.False(result.RequiresStructuredOutput);
        Assert.Equal("explain_result", result.SchemaName);
    }

    [Theory]
    [InlineData("Give me the lyrics for Closer to the Heart by Rush.", ActivityKind.Explain, 0.60, "explain_result")]
    [InlineData("The build failed with CS0103 and MSB1009 after the last change.", ActivityKind.BuildFix, 0.92, "build_fix_result")]
    [InlineData("Two xUnit tests failed after the refactor.", ActivityKind.TestFailureAnalysis, 0.92, "test_failure_analysis_result")]
    [InlineData("Please do a deep code review of this pull request.", ActivityKind.DeepCodeReview, 0.90, "deep_code_review_result")]
    [InlineData("We need to harden the app against SSRF and secret leakage.", ActivityKind.SecurityReview, 0.84, "security_review_result")]
    [InlineData("The LM Studio bridge is hanging on POST /v1/chat/completions.", ActivityKind.Diagnostic, 0.88, "diagnostic_result")]
    [InlineData("Please make a plan for the Blazor extension architecture.", ActivityKind.ImplementationPlan, 0.84, "implementation_plan_result")]
    public async Task RouteAsync_Should_Classify_Common_Prompt_Matrix(
        string request,
        ActivityKind expectedActivity,
        double expectedConfidence,
        string expectedSchema)
    {
        var router = new RuleFirstActivityRouter(new ActivityProfileRegistry());

        var result = await router.RouteAsync(request, CancellationToken.None);

        Assert.Equal(expectedActivity, result.Activity);
        Assert.Equal(expectedConfidence, result.Confidence, precision: 2);
        Assert.Equal(expectedSchema, result.SchemaName);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
