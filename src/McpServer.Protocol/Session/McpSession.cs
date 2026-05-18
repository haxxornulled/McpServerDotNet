using LanguageExt;
using LanguageExt.Common;
using McpServer.Protocol.Lifecycle;
using McpServer.Protocol.Roots;

namespace McpServer.Protocol.Session;

public sealed class McpSession
{
    private readonly object _syncRoot = new();

    private int _initializeCompleted;
    private int _ready;
    private int _shutdownRequested;

    private string? _protocolVersion;
    private ClientCapabilitiesDto? _clientCapabilities;

    private IReadOnlyList<RootDto> _clientRoots =
        global::System.Array.AsReadOnly(global::System.Array.Empty<RootDto>());

    public string? ProtocolVersion
    {
        get
        {
            lock (_syncRoot)
            {
                return _protocolVersion;
            }
        }
    }

    public ClientCapabilitiesDto? ClientCapabilities
    {
        get
        {
            lock (_syncRoot)
            {
                return _clientCapabilities;
            }
        }
    }

    public IReadOnlyList<RootDto> ClientRoots
    {
        get
        {
            lock (_syncRoot)
            {
                return _clientRoots;
            }
        }
    }

    public bool IsInitialized
    {
        get
        {
            return Volatile.Read(ref _initializeCompleted) == 1;
        }
    }

    public bool IsReady
    {
        get
        {
            return Volatile.Read(ref _ready) == 1;
        }
    }

    public bool IsShutdownRequested
    {
        get
        {
            return Volatile.Read(ref _shutdownRequested) == 1;
        }
    }

    public bool SupportsRoots
    {
        get
        {
            lock (_syncRoot)
            {
                return _clientCapabilities?.Roots is not null;
            }
        }
    }

    public Fin<Unit> CompleteInitialize(
        string protocolVersion,
        ClientCapabilitiesDto? clientCapabilities)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            return Error.New("Protocol version is required");
        }

        lock (_syncRoot)
        {
            if (_initializeCompleted == 1)
            {
                return Error.New("Session already initialized");
            }

            _protocolVersion = protocolVersion;
            _clientCapabilities = clientCapabilities ?? ClientCapabilitiesDto.None;

            Volatile.Write(ref _initializeCompleted, 1);
        }

        return Prelude.unit;
    }

    public Fin<Unit> UpdateClientRoots(IReadOnlyList<RootDto>? clientRoots)
    {
        IReadOnlyList<RootDto> snapshot = CreateRootSnapshot(clientRoots);

        lock (_syncRoot)
        {
            if (_initializeCompleted != 1)
            {
                return Error.New("Initialize must complete first");
            }

            _clientRoots = snapshot;
        }

        return Prelude.unit;
    }

    public Fin<Unit> MarkReady()
    {
        lock (_syncRoot)
        {
            if (_initializeCompleted != 1)
            {
                return Error.New("Initialize must complete first");
            }

            if (_ready == 1)
            {
                return Error.New("Session already ready");
            }

            Volatile.Write(ref _ready, 1);
        }

        return Prelude.unit;
    }

    public Fin<Unit> RequestShutdown()
    {
        lock (_syncRoot)
        {
            if (_initializeCompleted != 1)
            {
                return Error.New("Cannot shutdown before initialization");
            }

            if (_shutdownRequested == 1)
            {
                return Error.New("Shutdown already requested");
            }

            Volatile.Write(ref _shutdownRequested, 1);
        }

        return Prelude.unit;
    }

    private static IReadOnlyList<RootDto> CreateRootSnapshot(
        IReadOnlyList<RootDto>? clientRoots)
    {
        if (clientRoots is null || clientRoots.Count == 0)
        {
            return global::System.Array.AsReadOnly(
                global::System.Array.Empty<RootDto>());
        }

        return global::System.Array.AsReadOnly(clientRoots.ToArray());
    }
}
