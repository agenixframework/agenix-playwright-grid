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

using Agenix.PlaywrightGrid.Shared.Logging;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

/// <summary>
///     Background service that periodically verifies the worker is registered with the hub.
///     This is the "slow path" detection mechanism that complements timer gap detection.
///     If the worker is not found in the hub's worker list, triggers re-registration.
/// </summary>
public sealed class WorkerRegistrationVerifier(
    WorkerServiceRunner runner,
    WorkerOptions options,
    IHubClient hubClient,
    ChunkedLogger<WorkerRegistrationVerifier>? chunkedLogger = null)
    : BackgroundService
{
    private readonly ChunkedLogger<WorkerRegistrationVerifier>? _chunkedLogger = chunkedLogger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.RegistrationVerificationIntervalSeconds);

        _chunkedLogger?.LogInformation(
            null,
            "[VerifyRegistration] Starting periodic verification (interval: {IntervalSeconds}s)",
            options.RegistrationVerificationIntervalSeconds);

        // Wait one interval before first check (give worker time to register on startup)
        try
        {
            await Task.Delay(interval, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // Shutting down before first check
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerifyRegistrationAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _chunkedLogger?.LogError(ex,
                    null,
                    "[VerifyRegistration] Unexpected error during verification: {Message}",
                    ex.Message);
            }

            // Wait for the next verification cycle
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _chunkedLogger?.LogInformation(null, "[VerifyRegistration] Stopped");
    }

    private async Task VerifyRegistrationAsync(CancellationToken ct)
    {
        using var op = _chunkedLogger?.BeginOperation("VerifyRegistration");
        _chunkedLogger?.LogMilestone(EventCodes.Worker.RegistrationVerificationStarted, "Checking worker registration status with hub...");

        try
        {
            // Call hub diagnostics endpoint
            var diagnostics = await hubClient.GetDiagnosticsAsync(options.HubUrl, ct);
            if (diagnostics == null)
            {
                _chunkedLogger?.LogWarning(
                    EventCodes.Worker.RegistrationVerificationFailed,
                    "[VerifyRegistration] Failed to retrieve hub diagnostics (hub may be unreachable)");
                op?.Fail(new Exception("Failed to retrieve hub diagnostics"), ErrorType.DependencyFailure, DependencyName.Hub);
                return;
            }

            // Check if current worker exists in a worker list
            var workerExists = diagnostics.Workers?.Any(w =>
                string.Equals(w.Id, options.NodeId, StringComparison.OrdinalIgnoreCase)) ?? false;

            if (!workerExists)
            {
                _chunkedLogger?.LogWarning(
                    EventCodes.Worker.RegistrationVerificationFailed,
                    "[VerifyRegistration] Worker {NodeId} not found in hub worker list. Triggering re-registration...",
                    options.NodeId);

                try
                {
                    await runner.EnsureRegisteredAsync("periodic_verification");
                    _chunkedLogger?.LogMilestone(
                        EventCodes.Worker.RegistrationVerificationSucceeded,
                        "[VerifyRegistration] Re-registration completed successfully");
                }
                catch (Exception ex)
                {
                    _chunkedLogger?.LogError(ex,
                        EventCodes.Worker.RegistrationVerificationFailed,
                        "[VerifyRegistration] Failed to re-register: {Message}",
                        ex.Message);
                    op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Hub);
                    return;
                }
            }
            else
            {
                _chunkedLogger?.LogMilestone(
                    EventCodes.Worker.RegistrationVerificationSucceeded,
                    "[VerifyRegistration] Worker {NodeId} found in hub worker list (OK)",
                    options.NodeId);
            }
            op?.Complete();
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogError(ex,
                EventCodes.Worker.RegistrationVerificationFailed,
                "[VerifyRegistration] Error during verification: {Message}",
                ex.Message);
            op?.Fail(ex, ErrorType.Unexpected);
        }
    }
}
