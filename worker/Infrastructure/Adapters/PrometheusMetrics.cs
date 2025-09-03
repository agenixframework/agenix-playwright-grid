#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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
        "worker_playwright_version_mismatch", "1 if Playwright version mismatch on node, else 0", "node", "expected", "actual");

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
}
