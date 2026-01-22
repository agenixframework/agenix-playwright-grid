#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

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

    private static readonly Gauge PwVersionMismatch = Metrics.CreateGauge(
        "worker_playwright_version_mismatch", "1 if Playwright version mismatch on node, else 0", "node", "expected",
        "actual");

    private static readonly Counter ReRegistrationCount = Metrics.CreateCounter(
        "worker_re_registrations_total",
        "Number of worker re-registrations (recovery after hub loss or system sleep)",
        "node", "trigger");

    private static readonly Counter ReRegistrationErrorCount = Metrics.CreateCounter(
        "worker_re_registration_errors_total",
        "Number of failed worker re-registration attempts",
        "node", "trigger");

    private static readonly Counter BrowserHealthCheckCount = Metrics.CreateCounter(
        "worker_browser_health_check_total",
        "Number of browser health checks performed",
        "node", "label", "browser", "result");

    private static readonly Histogram BrowserHealthCheckDuration = Metrics.CreateHistogram(
        "worker_browser_health_check_duration_seconds",
        "Duration of browser health checks in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "node" },
            Buckets = new[] { 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 }
        });

    private static readonly Histogram BrowserRecycleLatency = Metrics.CreateHistogram(
        "worker_browser_recycle_latency_seconds",
        "Latency from recycle flag set to actual recycle completion in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "node", "label" },
            Buckets = new[] { 1.0, 2.5, 5.0, 10.0, 15.0, 30.0, 60.0, 120.0 }
        });

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

    public void SetPlaywrightVersionMismatch(string nodeId, string expected, string actual, int mismatch)
    {
        PwVersionMismatch.WithLabels(nodeId, expected ?? string.Empty, actual ?? string.Empty).Set(mismatch);
    }

    public void IncrementReRegistration(string nodeId, string trigger)
    {
        ReRegistrationCount.WithLabels(nodeId, trigger).Inc();
    }

    public void IncrementReRegistrationError(string nodeId, string trigger)
    {
        ReRegistrationErrorCount.WithLabels(nodeId, trigger).Inc();
    }

    public void RecordBrowserHealthCheck(string nodeId, string labelKey, string browserType, bool success)
    {
        var result = success ? "success" : "failure";
        BrowserHealthCheckCount.WithLabels(nodeId, labelKey, browserType, result).Inc();
    }

    public void RecordBrowserHealthCheckDuration(string nodeId, double durationSeconds)
    {
        BrowserHealthCheckDuration.WithLabels(nodeId).Observe(durationSeconds);
    }

    public void RecordBrowserRecycleLatency(string nodeId, string labelKey, double latencySeconds)
    {
        BrowserRecycleLatency.WithLabels(nodeId, labelKey).Observe(latencySeconds);
    }
}
