namespace WorkerService.Application.Ports;

public interface IMetricsPort
{
    void SetPoolCapacity(string nodeId, string labelKey, int count);
    void SetPoolAvailable(string nodeId, string labelKey, long count);
    void IncrementBorrow(string nodeId, string labelKey);
}
