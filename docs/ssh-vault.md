# SSH Vault

This page documents the shared `McpServer.Host` SSH vault and profile workflow.

## What the vault is for

The shared SSH tool uses named profiles for connection settings and a file-backed vault for secret material.

- Profiles live in `config/mcpserver/ssh-profiles.local.json`
- Secret payloads live in `config/mcpserver/ssh-vault.local.json`
- The vault key lives outside the repo in `%LOCALAPPDATA%\McpServer\ssh-vault.key`
- Profiles point at a vault item with `passwordVaultItemName`

That keeps credentials out of environment variables and out of the profile file itself.

## Files and paths

Default locations:

| Item | Path | Purpose |
| --- | --- | --- |
| Repo-local profile file | `config/mcpserver/ssh-profiles.local.json` | Live SSH profiles used by the shared host |
| Repo example profile file | `config/mcpserver/ssh-profiles.local.example.json` | Template to copy from |
| Repo-local vault file | `config/mcpserver/ssh-vault.local.json` | Encrypted SSH secrets |
| Repo example vault file | `config/mcpserver/ssh-vault.local.example.json` | Template to copy from |
| User-local profile file | `%LOCALAPPDATA%\McpServer\ssh-profiles.json` | Per-user overrides |
| Vault key file | `%LOCALAPPDATA%\McpServer\ssh-vault.key` | Key material used to protect vault entries |

Repo-local files are resolved from the host content root, so they work consistently from Visual Studio, VS Code, and `dotnet run`.

The repo ignores the live local JSON files and the transient vault lock file:

- `config/mcpserver/*.local.json`
- `config/mcpserver/*.lock`

Keep the example files checked in. Keep the live files machine-local.

## Quick start

1. Copy the example files if you do not already have live ones:

```text
Copy-Item .\config\mcpserver\ssh-profiles.local.example.json .\config\mcpserver\ssh-profiles.local.json
Copy-Item .\config\mcpserver\ssh-vault.local.example.json .\config\mcpserver\ssh-vault.local.json
```

2. Add a credential to the vault:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- add dev --secret-file .\secrets\dev-password.txt --description "Standard dev credential"
```

3. Point the profile at the named vault item:

```json
{
  "name": "dev",
  "host": "192.168.1.50",
  "port": 22,
  "username": "deploy",
  "passwordVaultItemName": "dev"
}
```

4. Use the shared host SSH tool for real SSH execution. The host resolves `passwordVaultItemName` and `privateKeyPassphraseVaultItemName` from the vault and does not use environment-variable-backed secrets.

## Common commands

These are the commands you will usually run in order:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- add root --description "SSH password for root"
dotnet run --project .\tools\McpServer.SshVaultCli -- profile upsert root --host 173.255.205.169 --username root --password-vault-item root --working-directory /root --host-key-sha256 SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI --allow-sudo-command true --allow-all-commands true
dotnet run --project .\tools\McpServer.SshVaultCli -- profile link root --password-vault-item root
dotnet run --project .\tools\McpServer.SshVaultCli -- profile list
dotnet run --project .\tools\McpServer.SshVaultCli -- verify root
cmd.exe /c "set MCPSERVER_INTEGRATION_LIVE_SSH=1&& dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~Ssh -v minimal"
```

If you rotate the password later, repeat `vault add root` and then `profile link root --password-vault-item root`.

## CLI reference

The vault CLI is:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- help
```

The profile CLI is part of the same executable:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- profile help
```

### `add`

Adds or updates one named vault entry.

Usage:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- add <name> [--secret value] [--secret-file path] [--description text] [--vault-path path] [--vault-key-path path] [--base-directory path]
```

Options:

- `--secret` stores plaintext passed directly on the command line.
- `--secret-file` reads the plaintext secret from a file.
- `--description` stores a friendly label for the entry.
- `--vault-path` overrides the vault JSON file location.
- `--vault-key-path` overrides the local vault key file location.
- `--base-directory` resolves relative paths against an alternate workspace root.

If neither `--secret` nor `--secret-file` is provided, the CLI prompts interactively.
If stdin is redirected, you must provide one of those options explicitly.

### `verify`

Verifies that the stored secret decrypts correctly and, if you supply an expected value, that it matches exactly.
The secret is never printed.

Usage:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- verify <name> [--expected value] [--expected-file path] [--vault-path path] [--vault-key-path path] [--base-directory path]
```

Options:

- `--expected` compares against plaintext passed directly on the command line.
- `--expected-file` reads the expected plaintext secret from a file.
- `--vault-path` overrides the vault JSON file location.
- `--vault-key-path` overrides the local vault key file location.
- `--base-directory` resolves relative paths against an alternate workspace root.

If neither `--expected` nor `--expected-file` is provided, the CLI prompts interactively.
If stdin is redirected, you must provide one of those options explicitly.
For everyday use, prefer the secure prompt or `--expected-file` instead of placing secrets on the command line.

### `list`

Lists the stored item names and descriptions.

Usage:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- list
```

### `delete`

Deletes one named vault entry.

Usage:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- delete <name>
```

If the name does not exist, the CLI returns a non-zero exit code.

## SSH profiles

Profiles are the user-facing mapping between a human/account, the host, and the credential reference. The vault stores the encrypted secret; the profile stores the SSH login shape.

Use the profile commands to add or update that mapping:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- profile upsert root --host 173.255.205.169 --username root --password-vault-item root --working-directory /root --host-key-sha256 SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI --allow-sudo-command true --allow-all-commands true
```

Useful commands:

- `profile list` shows the available SSH profiles.
- `profile show <name>` prints one profile as readable JSON.
- `profile upsert <name>` creates or updates a profile.
- `profile link <name>` updates the credential reference on an existing profile.
- `profile unlink <name>` clears the credential references on an existing profile.
- `profile delete <name>` removes a profile.
- `vault add <name>` adds or updates the encrypted secret for a profile.
- `vault verify <name>` confirms the stored secret decrypts correctly.
- `vault delete <name>` removes the encrypted secret.

The important field is `passwordVaultItemName` or `privateKeyPassphraseVaultItemName`. Those link the profile to a named vault entry.

## Live SSH integration test

To exercise the real SSH login path against the repo-local profile and vault, set:

```text
MCPSERVER_INTEGRATION_LIVE_SSH=1
```

Then run:

```text
dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Live_Ssh_Tool_Should_Login_And_Parse_Result_On_Real_Host
```

Optional overrides:

```text
MCPSERVER_INTEGRATION_LIVE_SSH_PROFILE=root
MCPSERVER_INTEGRATION_LIVE_SSH_COMMAND=whoami
MCPSERVER_INTEGRATION_LIVE_SSH_EXPECTED_STDOUT=root
MCPSERVER_INTEGRATION_LIVE_SSH_EXPECTED_HOST=173.255.205.169
```

## End-to-end workflow

The usual workflow is:

1. Create or copy a live profile file under `config/mcpserver/ssh-profiles.local.json`.
2. Add a matching secret to `config/mcpserver/ssh-vault.local.json` with the vault CLI.
3. Point the profile at the named item with `passwordVaultItemName`.
4. Run the shared host SSH tool for production SSH access. If you need the AgentRouter smoke harness to exercise the same vault-backed SSH profiles, run the typed C# harness against the host.

Example:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- add dev-admin --secret-file .\secrets\dev-admin.txt --description "Privileged SSH credential"
```

Then the profile can reference:

```json
"passwordVaultItemName": "dev-admin"
```

## Privileged profiles

Keep privileged access explicit.

- Use a separate profile for admin workflows, for example `dev-admin`.
- Set `AllowSudoCommand=true` only on that explicit privileged profile.
- Set `AllowAllCommands=true` only when the profile is intentionally unrestricted, such as a trusted root admin profile.
- Keep the default profile narrow.

That way a user with sudo can still administer a server, but the privilege boundary is obvious and auditable.

## Path overrides

Use `--base-directory` when the CLI is running against an alternate workspace or a test fixture:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- list --base-directory D:\Work\OtherRepo
```

Use `--vault-path` and `--vault-key-path` when you need to point the CLI at custom files:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- add dev --secret "..." --vault-path D:\Temp\ssh-vault.json --vault-key-path D:\Temp\ssh-vault.key
```

Relative paths are resolved against `--base-directory`, not the process current directory.

## Security model

- Secrets are stored encrypted at rest, not in plaintext.
- The vault format includes the encrypted payload plus the metadata needed to restore it.
- The vault key stays outside the repo in the user profile area.
- The live vault file is file-locked while it is being modified.
- Writes are atomic replace-on-write operations.
- The lock file is transient and should not be committed.

This is a file-backed developer vault, not a hardware security module or enterprise secrets manager.

## Troubleshooting

- If `list` shows no entries, check that you are pointing at the same `--vault-path` and `--base-directory` that `add` used.
- If `add` fails with a redirection error, pass `--secret` or `--secret-file` explicitly.
- If a profile says it uses a vault item but the SSH tool cannot resolve it, verify `passwordVaultItemName` or `privateKeyPassphraseVaultItemName` matches the entry name exactly.
- If you want a clean portable test fixture, use temporary `--vault-path` and `--vault-key-path` values in the test directory.
