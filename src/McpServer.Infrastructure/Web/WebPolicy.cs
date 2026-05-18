using System.Net;
using System.Net.Sockets;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Web;
using static LanguageExt.Prelude;

namespace McpServer.Infrastructure.Web;

public sealed class WebPolicy : IWebPolicy
{
    private static readonly System.Collections.Generic.HashSet<string> AlwaysBlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "localhost.localdomain",
        "metadata.google.internal"
    };

    private readonly IReadOnlySet<string> _allowedHosts;
    private readonly bool _allowLocalLoopbackHosts;

    public WebPolicy(
        IReadOnlySet<string>? allowedHosts = null,
        bool allowLocalLoopbackHosts = false,
        string searchBaseUrl = "https://duckduckgo.com/html/?q=")
    {
        _allowedHosts = allowedHosts ?? new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _allowLocalLoopbackHosts = allowLocalLoopbackHosts;
        SearchBaseUrl = string.IsNullOrWhiteSpace(searchBaseUrl)
            ? "https://duckduckgo.com/html/?q="
            : searchBaseUrl;
    }

    public int MaxResponseBytes => 512 * 1024;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);
    public int MaxRedirects => 5;
    public string SearchBaseUrl { get; }

    public Fin<Unit> ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Error.New($"Invalid URL: {url}");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Error.New("Only HTTP and HTTPS are supported.");
        }

        return ValidateHost(uri.Host);
    }

    public Fin<Unit> ValidateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Error.New("Host is required.");
        }

        var normalizedHost = host.Trim().TrimEnd('.');
        if (! _allowLocalLoopbackHosts && AlwaysBlockedHosts.Contains(normalizedHost))
        {
            return Error.New($"Host is blocked by web access policy: {host}");
        }

        if (!_allowLocalLoopbackHosts && IPAddress.TryParse(normalizedHost, out var address) && IsPrivateOrLocalAddress(address))
        {
            return Error.New($"Host resolves to a private, local, or reserved address: {host}");
        }

        if (_allowedHosts.Count == 0)
        {
            return Error.New("Web access requires an explicit host allowlist.");
        }

        return _allowedHosts.Contains(normalizedHost)
            ? unit
            : Error.New($"Host not allowed: {host}");
    }

    public Fin<Unit> ValidateResolvedAddresses(string host, IReadOnlyCollection<IPAddress> addresses)
    {
        if (addresses.Count == 0)
        {
            return Error.New($"Host did not resolve to any addresses: {host}");
        }

        if (_allowLocalLoopbackHosts)
        {
            return unit;
        }

        foreach (var address in addresses)
        {
            if (IsPrivateOrLocalAddress(address))
            {
                return Error.New($"Host '{host}' resolved to blocked address '{address}'.");
            }
        }

        return unit;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 => true,
                10 => true,
                100 when bytes[1] is >= 64 and <= 127 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                198 when bytes[1] is 18 or 19 => true,
                224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232 or 233 or 234 or 235 or 236 or 237 or 238 or 239 => true,
                >= 240 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6Multicast ||
                   address.IsIPv6SiteLocal ||
                   IsUniqueLocalIpv6(address);
        }

        return true;
    }

    private static bool IsUniqueLocalIpv6(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length > 0 && (bytes[0] & 0xfe) == 0xfc;
    }
}
