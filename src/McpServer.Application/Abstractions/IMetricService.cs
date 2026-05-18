using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IMetricService
    {
        void IncrementCounter(string name, long value = 1);
        void RecordHistogram(string name, double value);
        void SetGauge(string name, double value);
    }
}