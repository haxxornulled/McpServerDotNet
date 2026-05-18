using System.Net;
using McpServer.Infrastructure.Web;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class WebPolicySsrHardeningTests
{
    [Fact]
    public void ValidateUrl_Should_Block_File_Scheme()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateUrl("file:///C:/Windows/win.ini");

        Assert.True(result.IsFail);
        Assert.Contains("HTTP and HTTPS", GetError(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateHost_Should_Require_Host()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateHost("   ");

        Assert.True(result.IsFail);
        Assert.Contains("Host is required", GetError(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateHost_Should_Trim_Trailing_Dot()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateHost("example.com.");

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void ValidateHost_Should_Block_Localhost_With_Trailing_Dot()
    {
        var sut = CreatePolicy("localhost");

        var result = sut.ValidateHost("localhost.");

        Assert.True(result.IsFail);
        Assert.Contains("blocked", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHost_Should_Block_Cloud_Metadata_Ip()
    {
        var sut = CreatePolicy("169.254.169.254");

        var result = sut.ValidateHost("169.254.169.254");

        Assert.True(result.IsFail);
        Assert.Contains("private", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHost_Should_Block_Cgnat_Range()
    {
        var sut = CreatePolicy("100.64.1.1");

        var result = sut.ValidateHost("100.64.1.1");

        Assert.True(result.IsFail);
        Assert.Contains("private", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHost_Should_Block_Benchmark_Network_Range()
    {
        var sut = CreatePolicy("198.18.0.1");

        var result = sut.ValidateHost("198.18.0.1");

        Assert.True(result.IsFail);
        Assert.Contains("private", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHost_Should_Block_Multicast_Range()
    {
        var sut = CreatePolicy("224.0.0.1");

        var result = sut.ValidateHost("224.0.0.1");

        Assert.True(result.IsFail);
        Assert.Contains("private", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateResolvedAddresses_Should_Fail_When_Dns_Returns_No_Addresses()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateResolvedAddresses("example.com", Array.Empty<IPAddress>());

        Assert.True(result.IsFail);
        Assert.Contains("did not resolve", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateResolvedAddresses_Should_Block_Ipv6_Unique_Local()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateResolvedAddresses("example.com", [IPAddress.Parse("fc00::1")]);

        Assert.True(result.IsFail);
        Assert.Contains("blocked address", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateResolvedAddresses_Should_Block_Ipv6_Link_Local()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateResolvedAddresses("example.com", [IPAddress.Parse("fe80::1")]);

        Assert.True(result.IsFail);
        Assert.Contains("blocked address", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateResolvedAddresses_Should_Allow_Public_Resolved_Address()
    {
        var sut = CreatePolicy("example.com");

        var result = sut.ValidateResolvedAddresses("example.com", [IPAddress.Parse("93.184.216.34")]);

        Assert.True(result.IsSucc, GetError(result));
    }

    private static WebPolicy CreatePolicy(params string[] hosts)
    {
        return new WebPolicy(new System.Collections.Generic.HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase));
    }

    private static string GetError(LanguageExt.Fin<LanguageExt.Unit> result)
    {
        return result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message);
    }
}
