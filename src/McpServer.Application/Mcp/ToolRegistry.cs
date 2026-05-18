using McpServer.Application.Abstractions.Mcp;

namespace McpServer.Application.Mcp
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, IToolHandler<object>> _handlers;

        public ToolRegistry(IEnumerable<IToolHandler<object>> handlers)
        {
            _handlers = handlers.ToDictionary(h => h.Name, h => h);
        }

        public bool TryGetHandler(string name, out IToolHandler<object> handler)
        {
            return _handlers.TryGetValue(name, out handler);
        }

        public IReadOnlyList<string> GetAvailableTools()
        {
            return _handlers.Keys.ToList();
        }
    }
}