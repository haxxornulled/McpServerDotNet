using System.Net;
using McpServer.Infrastructure.Web;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class WebPolicyTests
{
    [Fact]
    public void ValidateUrl_Should_Require_Explicit_Host_Allowlist()
    {
        var sut = new WebPolicy();

        var result = sut.ValidateUrl("https://example.com");

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected URL validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("explicit host allowlist", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateUrl_Should_Block_Localhost_Even_When_Allowlisted()
    {
        var sut = new WebPolicy(new System.Collections.Generic.HashSet<string>(["localhost"], StringComparer.OrdinalIgnoreCase));

        var result = sut.ValidateUrl("http://localhost:5126/health");

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected URL validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("blocked", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUrl_Should_Allow_Localhost_When_Explicitly_Enabled()
    {
        var sut = new WebPolicy(
            new System.Collections.Generic.HashSet<string>(["localhost"], StringComparer.OrdinalIgnoreCase),
            allowLocalLoopbackHosts: true);

        var result = sut.ValidateUrl("http://localhost:5126/health");

        Assert.True(result.IsSucc, result.Match(
            Succ: _ => string.Empty,
            Fail: failure => failure.Message));
    }

    [Fact]
    public void ValidateResolvedAddresses_Should_Block_Private_Addresses()
    {
        var sut = new WebPolicy(new System.Collections.Generic.HashSet<string>(["example.com"], StringComparer.OrdinalIgnoreCase));

        var result = sut.ValidateResolvedAddresses("example.com", [IPAddress.Parse("127.0.0.1")]);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected DNS validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("blocked address", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUrl_Should_Allow_Explicit_Public_Host()
    {
        var sut = new WebPolicy(new System.Collections.Generic.HashSet<string>(["example.com"], StringComparer.OrdinalIgnoreCase));

        var result = sut.ValidateUrl("https://example.com/docs");

        Assert.True(result.IsSucc);
    }
}
