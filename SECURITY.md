# MCPServer Security Model

MCPServer is designed for local, defensive developer automation. The official project must stay safe by default, explicit when risky capabilities are enabled, and auditable when tools perform writes, network access, or process execution.

## Default posture

By default:

- shell execution is disabled;
- SSH execution is disabled;
- web access is disabled;
- web access requires an explicit host allowlist when enabled;
- shell execution requires an explicit command allowlist when enabled;
- SSH command execution requires profile-level command allowlists;
- filesystem access is bounded to configured workspace roots;
- destructive filesystem operations refuse workspace/project roots and protected repository/security metadata paths.

MCP clients, prompts, and model instructions must not be able to enable high-risk capabilities at runtime. Only local configuration can enable them.

## High-risk capability rules

### Shell

`shell.exec` is disabled unless `McpServer:Shell:Enabled` is true. When enabled, `AllowedCommands` must be non-empty. Empty allowlist means deny, not allow.

The default denylist includes shells, PowerShell, Windows LOLBins commonly used for abuse, destructive filesystem commands, shutdown/reboot commands, and network transfer utilities. Denylist checks win over allowlist checks.

Bare shell command lines and Windows PowerShell compatibility wrapping are separately disabled unless explicitly enabled.

### Web

`web.fetch_url` and `web.search` are disabled unless `McpServer:WebAccess:Enabled` is true. When enabled, `AllowedHosts` must be non-empty.

The web policy blocks localhost, private address ranges, link-local addresses, cloud metadata-style endpoints, multicast/reserved ranges, and private DNS resolutions. Redirects are handled manually and revalidated before following.

### SSH

`ssh.execute` and `ssh.write_text` are disabled unless `McpServer:Ssh:Enabled` is true and at least one profile is configured.

Profiles should pin `HostKeySha256`. `AcceptUnknownHostKey` exists only for isolated lab use and should remain false for normal use. Command execution requires a profile-level `AllowedCommands` list. Remote file writes require either `AllowedRemotePathPrefixes` or a profile `WorkingDirectory`.

### Filesystem

Filesystem tools normalize paths through the workspace path policy. Destructive operations are additionally checked by `IDestructiveFileOperationPolicy`.

Protected targets include:

- workspace/project/allowed roots;
- `.git`, `.hg`, `.svn`, `.ssh` paths;
- `.env`;
- `appsettings.Production.json`;
- `Directory.Build.props`;
- `Directory.Packages.props`;
- key/certificate file extensions such as `.pem`, `.key`, `.pfx`, and `.p12`.

Recursive directory delete requires an exact confirmation string returned by the policy error.

## Not supported

This project does not support malware development, credential theft, persistence, evasion, unauthorized access, lateral movement, exploit execution against third-party systems, or abuse of legitimate administration tools.
