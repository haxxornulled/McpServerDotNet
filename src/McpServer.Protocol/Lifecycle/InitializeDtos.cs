using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace McpServer.Protocol.Lifecycle;

public sealed class InitializeRequestDto
{
    [JsonConstructor]
    public InitializeRequestDto(
        string ProtocolVersion,
        ClientCapabilitiesDto? Capabilities,
        ClientInfoDto? ClientInfo)
    {
        this.ProtocolVersion = ProtocolVersion ?? string.Empty;
        this.Capabilities = Capabilities ?? ClientCapabilitiesDto.None;
        this.ClientInfo = ClientInfo ?? ClientInfoDto.Unknown;
    }

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; }

    [JsonPropertyName("capabilities")]
    public ClientCapabilitiesDto Capabilities { get; }

    [JsonPropertyName("clientInfo")]
    public ClientInfoDto ClientInfo { get; }
}

public sealed class ClientInfoDto
{
    public static ClientInfoDto Unknown { get; } = new("unknown", "unknown");

    [JsonConstructor]
    public ClientInfoDto(string Name, string Version)
    {
        this.Name = string.IsNullOrWhiteSpace(Name) ? "unknown" : Name;
        this.Version = string.IsNullOrWhiteSpace(Version) ? "unknown" : Version;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("version")]
    public string Version { get; }
}

public sealed class ClientCapabilitiesDto
{
    public static ClientCapabilitiesDto None { get; } = new();

    [JsonConstructor]
    public ClientCapabilitiesDto(
        RootsClientCapabilityDto? Roots = null,
        object? Sampling = null,
        object? Elicitation = null)
    {
        this.Roots = Roots;
        this.Sampling = Sampling;
        this.Elicitation = Elicitation;
    }

    [JsonPropertyName("roots")]
    public RootsClientCapabilityDto? Roots { get; }

    [JsonPropertyName("sampling")]
    public object? Sampling { get; }

    [JsonPropertyName("elicitation")]
    public object? Elicitation { get; }
}

public sealed class RootsClientCapabilityDto
{
    public static RootsClientCapabilityDto Disabled { get; } = new(false);

    public static RootsClientCapabilityDto Enabled { get; } = new(true);

    [JsonConstructor]
    public RootsClientCapabilityDto(bool ListChanged = false)
    {
        this.ListChanged = ListChanged;
    }

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; }
}

public sealed class InitializeResultDto
{
    [JsonConstructor]
    public InitializeResultDto(
        string ProtocolVersion,
        ServerCapabilitiesDto Capabilities,
        ServerInfoDto ServerInfo)
    {
        this.ProtocolVersion = ProtocolVersion ?? string.Empty;
        this.Capabilities = Capabilities ?? ServerCapabilitiesDto.Empty;
        this.ServerInfo = ServerInfo ?? ServerInfoDto.Default;
    }

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; }

    [JsonPropertyName("capabilities")]
    public ServerCapabilitiesDto Capabilities { get; }

    [JsonPropertyName("serverInfo")]
    public ServerInfoDto ServerInfo { get; }
}

public sealed class ServerInfoDto
{
    public static ServerInfoDto Default { get; } = new("McpServer.FileSystem", "0.1.1");

    [JsonConstructor]
    public ServerInfoDto(string Name, string Version)
    {
        this.Name = string.IsNullOrWhiteSpace(Name) ? "unknown" : Name;
        this.Version = string.IsNullOrWhiteSpace(Version) ? "unknown" : Version;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("version")]
    public string Version { get; }
}

public sealed class ServerCapabilitiesDto
{
    public static ServerCapabilitiesDto Empty { get; } = new(
        Tools: null,
        Resources: null,
        Prompts: null,
        Experimental: null);

    [JsonConstructor]
    public ServerCapabilitiesDto(
        ToolsCapabilityDto? Tools,
        ResourcesCapabilityDto? Resources,
        PromptsCapabilityDto? Prompts,
        IReadOnlyDictionary<string, object?>? Experimental = null)
    {
        this.Tools = Tools;
        this.Resources = Resources;
        this.Prompts = Prompts;
        this.Experimental = FreezeDictionary(Experimental);
    }

    [JsonPropertyName("tools")]
    public ToolsCapabilityDto? Tools { get; }

    [JsonPropertyName("resources")]
    public ResourcesCapabilityDto? Resources { get; }

    [JsonPropertyName("prompts")]
    public PromptsCapabilityDto? Prompts { get; }

    [JsonPropertyName("experimental")]
    public IReadOnlyDictionary<string, object?>? Experimental { get; }

    private static IReadOnlyDictionary<string, object?>? FreezeDictionary(
        IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }

        return new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(source, StringComparer.Ordinal));
    }
}

public sealed class ToolsCapabilityDto
{
    public static ToolsCapabilityDto StaticList { get; } = new(false);

    [JsonConstructor]
    public ToolsCapabilityDto(bool ListChanged)
    {
        this.ListChanged = ListChanged;
    }

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; }
}

public sealed class ResourcesCapabilityDto
{
    public static ResourcesCapabilityDto StaticListNoSubscribe { get; } = new(
        Subscribe: false,
        ListChanged: false);

    [JsonConstructor]
    public ResourcesCapabilityDto(bool Subscribe, bool ListChanged)
    {
        this.Subscribe = Subscribe;
        this.ListChanged = ListChanged;
    }

    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; }

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; }
}

public sealed class PromptsCapabilityDto
{
    public static PromptsCapabilityDto StaticList { get; } = new(false);

    [JsonConstructor]
    public PromptsCapabilityDto(bool ListChanged)
    {
        this.ListChanged = ListChanged;
    }

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; }
}
