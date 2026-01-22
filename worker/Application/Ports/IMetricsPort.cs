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

namespace WorkerService.Application.Ports;

public interface IMetricsPort
{
    void SetPoolCapacity(string nodeId, string labelKey, int count);
    void SetPoolAvailable(string nodeId, string labelKey, long count);
    void IncrementBorrow(string nodeId, string labelKey);
    void SetPlaywrightVersionMismatch(string nodeId, string expected, string actual, int mismatch);

    /// <summary>
    ///     Increments the counter for successful worker re-registrations.
    /// </summary>
    /// <param name="nodeId">Worker node identifier</param>
    /// <param name="trigger">Re-registration trigger type: "gap_detection" or "periodic_verification"</param>
    void IncrementReRegistration(string nodeId, string trigger);

    /// <summary>
    ///     Increments the counter for failed worker re-registration attempts.
    /// </summary>
    /// <param name="nodeId">Worker node identifier</param>
    /// <param name="trigger">Re-registration trigger type: "gap_detection" or "periodic_verification"</param>
    void IncrementReRegistrationError(string nodeId, string trigger);

    /// <summary>
    ///     Records a browser health check result (success or failure).
    /// </summary>
    /// <param name="nodeId">Worker node identifier</param>
    /// <param name="labelKey">Browser pool label key</param>
    /// <param name="browserType">Browser type (chromium, firefox, webkit)</param>
    /// <param name="success">True if health check passed, false if failed</param>
    void RecordBrowserHealthCheck(string nodeId, string labelKey, string browserType, bool success);

    /// <summary>
    ///     Records the duration of a browser health check in seconds.
    /// </summary>
    /// <param name="nodeId">Worker node identifier</param>
    /// <param name="durationSeconds">Health check duration in seconds</param>
    void RecordBrowserHealthCheckDuration(string nodeId, double durationSeconds);

    /// <summary>
    ///     Records the latency from when a recycle flag is set to when the browser is actually recycled.
    ///     This measures the effectiveness of ReconcileLoop responsiveness.
    /// </summary>
    /// <param name="nodeId">Worker node identifier</param>
    /// <param name="labelKey">Browser pool label key</param>
    /// <param name="latencySeconds">Time from flag set to recycle completion in seconds</param>
    void RecordBrowserRecycleLatency(string nodeId, string labelKey, double latencySeconds);
}
