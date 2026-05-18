using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IJobProvider
    {
        ValueTask<Fin<Unit>> ScheduleAsync<T>(T job, DateTimeOffset scheduledTime, CancellationToken ct);
        ValueTask<Fin<IReadOnlyList<T>>> GetScheduledJobsAsync<T>(CancellationToken ct);
    }
}