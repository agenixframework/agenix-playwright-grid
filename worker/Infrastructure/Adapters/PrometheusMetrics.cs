using Prometheus;
using WorkerService.Application.Ports;

namespace WorkerService.Infrastructure.Adapters;

public sealed class PrometheusMetrics : IMetricsPort
{
    private static readonly Gauge PoolCapacity = Metrics.CreateGauge(
        "worker_pool_capacity", "Worker pool capacity", "node", "label");

    private static readonly Gauge PoolAvailable = Metrics.CreateGauge(
        "worker_pool_available", "Worker pool available slots", "node", "label");

    private static readonly Counter BorrowCount = Metrics.CreateCounter(
        "worker_borrows_total", "Number of borrows", "node", "label");

    public void SetPoolCapacity(string nodeId, string labelKey, int count)
    {
        PoolCapacity.WithLabels(nodeId, labelKey).Set(count);
    }

    public void SetPoolAvailable(string nodeId, string labelKey, long count)
    {
        PoolAvailable.WithLabels(nodeId, labelKey).Set(count);
    }

    public void IncrementBorrow(string nodeId, string labelKey)
    {
        BorrowCount.WithLabels(nodeId, labelKey).Inc();
    }
}
