using System.Runtime.CompilerServices;
using LanguageExt;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;
using McpServer.Application.Ssh.Utils;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Ssh
{
    public class SshService : ISshService
    {
        private readonly ILogger<SshService> _logger;
        private readonly Dictionary<string, SshProfile> _profiles;

        public SshService(ILogger<SshService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profiles = new Dictionary<string, SshProfile>();
        }

        public ValueTask<Fin<SshCommandResult>> ExecuteAsync(ExecuteSshCommand command, CancellationToken ct)
        {
            // Hot path optimization - check for null/empty immediately
            if (string.IsNullOrWhiteSpace(command.Profile) || string.IsNullOrWhiteSpace(command.Command))
            {
                _logger.LogWarning("ExecuteAsync called with invalid parameters");
                return new ValueTask<Fin<SshCommandResult>>(Fin<SshCommandResult>.Fail(Error.New("Profile and command cannot be null or empty")));
            }

            try
            {
                _logger.LogInformation("Executing SSH command on profile {Profile}", command.Profile);

                if (!_profiles.TryGetValue(command.Profile, out var profile))
                {
                    return new ValueTask<Fin<SshCommandResult>>(Fin<SshCommandResult>.Fail(Error.New($"SSH profile not found: {command.Profile}")));
                }

                // In a real implementation, this would execute the actual SSH command
                // For now, we'll simulate the behavior with a mock response
                var result = new SshCommandResult(
                    profile.Name,
                    profile.Host,
                    profile.Port,
                    profile.Username,
                    command.Command,
                    command.WorkingDirectory,
                    0,
                    $"Output from {command.Command}",
                    string.Empty,
                    false,
                    false);

                _logger.LogInformation("Successfully executed SSH command on profile {Profile}", command.Profile);
                
                return new ValueTask<Fin<SshCommandResult>>(Fin<SshCommandResult>.Succ(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute SSH command on profile {Profile}", command.Profile);
                return new ValueTask<Fin<SshCommandResult>>(Fin<SshCommandResult>.Fail(Error.New($"Failed to execute SSH command: {ex.Message}")));
            }
        }

        public ValueTask<Fin<SshFileWriteResult>> WriteTextAsync(WriteSshTextCommand command, CancellationToken ct)
        {
            // Hot path optimization - check for null/empty immediately
            if (string.IsNullOrWhiteSpace(command.Profile) || string.IsNullOrWhiteSpace(command.Path) || string.IsNullOrWhiteSpace(command.Content))
            {
                _logger.LogWarning("WriteTextAsync called with invalid parameters");
                return new ValueTask<Fin<SshFileWriteResult>>(Fin<SshFileWriteResult>.Fail(Error.New("Profile, path and content cannot be null or empty")));
            }

            try
            {
                _logger.LogInformation("Writing text to SSH file on profile {Profile}", command.Profile);

                if (!_profiles.TryGetValue(command.Profile, out var profile))
                {
                    return new ValueTask<Fin<SshFileWriteResult>>(Fin<SshFileWriteResult>.Fail(Error.New($"SSH profile not found: {command.Profile}")));
                }

                // In a real implementation, this would write to the remote file
                // For now, we'll simulate the behavior with a mock response
                var result = new SshFileWriteResult(
                    profile.Name,
                    profile.Host,
                    profile.Port,
                    profile.Username,
                    command.Path,
                    command.Content.Length,
                    true);

                _logger.LogInformation("Successfully wrote text to SSH file on profile {Profile}", command.Profile);
                
                return new ValueTask<Fin<SshFileWriteResult>>(Fin<SshFileWriteResult>.Succ(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write text to SSH file on profile {Profile}", command.Profile);
                return new ValueTask<Fin<SshFileWriteResult>>(Fin<SshFileWriteResult>.Fail(Error.New($"Failed to write SSH file: {ex.Message}")));
            }
        }

        // Method to register SSH profiles (for testing or configuration purposes)
        public void RegisterProfile(SshProfile profile)
        {
            if (profile == null) 
                throw new ArgumentNullException(nameof(profile));
            
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new ArgumentException("Profile name cannot be null or empty", nameof(profile.Name));
            
            if (string.IsNullOrWhiteSpace(profile.Host))
                throw new ArgumentException("Profile host cannot be null or empty", nameof(profile.Host));
            
            if (string.IsNullOrWhiteSpace(profile.Username))
                throw new ArgumentException("Profile username cannot be null or empty", nameof(profile.Username));
            
            _profiles[profile.Name] = profile;
        }
    }
}

