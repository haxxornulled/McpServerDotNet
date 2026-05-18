using System.Net;
using LanguageExt;

namespace McpServer.Application.Abstractions.Web;

public interface IWebPolicy
{
    Fin<Unit> ValidateUrl(string url);
    Fin<Unit> ValidateHost(string host);
    Fin<Unit> ValidateResolvedAddresses(string host, IReadOnlyCollection<IPAddress> addresses);
    int MaxResponseBytes { get; }
    TimeSpan DefaultTimeout { get; }
    int MaxRedirects { get; }
    string SearchBaseUrl { get; }
}
