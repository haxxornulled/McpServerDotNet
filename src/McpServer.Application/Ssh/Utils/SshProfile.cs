namespace McpServer.Application.Ssh.Utils
{
    public sealed record SshProfile
    {
        public string Name { get; init; }
        public string Host { get; init; }
        public int Port { get; init; }
        public string Username { get; init; }
        public string? Password { get; init; }
        public string? KeyPath { get; init; }

        public SshProfile(
            string name,
            string host,
            int port,
            string username,
            string? password = null,
            string? keyPath = null)
        {
            Name = name;
            Host = host;
            Port = port;
            Username = username;
            Password = password;
            KeyPath = keyPath;
        }
    }
}
