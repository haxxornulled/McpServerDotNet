


namespace McpServer.Application.WebSearch
{
    // Example: Rate limiter for web search
    public interface IWebSearchRateLimiter
    {
        Task<bool> ShouldAllowAsync(string userId, CancellationToken ct);
    }

    public class InMemoryWebSearchRateLimiter : IWebSearchRateLimiter
    {
        private readonly int _maxPerMinute;
        private readonly object _lock = new();
        private DateTime _windowStart = DateTime.UtcNow;
        private int _count = 0;

        public InMemoryWebSearchRateLimiter(int maxPerMinute)
        {
            _maxPerMinute = maxPerMinute;
        }

        public Task<bool> ShouldAllowAsync(string userId, CancellationToken ct)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if ((now - _windowStart).TotalMinutes >= 1)
                {
                    _windowStart = now;
                    _count = 0;
                }
                if (_count < _maxPerMinute)
                {
                    _count++;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }
    }
}
