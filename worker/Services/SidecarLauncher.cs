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

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agenix.PlaywrightGrid.Shared.Logging;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public interface ISidecarLauncher
{
    Task<SidecarStartResult> StartAsync(string browserType, CancellationToken ct = default);
}

public readonly record struct SidecarStartResult(
    Process proc,
    string ws,
    string? playwrightVersion,
    string? browserVersion,
    string browser);

public sealed class SidecarLauncher(WorkerOptions options, ChunkedLogger<SidecarLauncher>? chunkedLogger = null) : ISidecarLauncher
{
    public async Task<SidecarStartResult> StartAsync(string browserType, CancellationToken ct = default)
    {
        using var op = chunkedLogger?.BeginOperation("SidecarLaunch", new Dictionary<string, object>
        {
            ["BrowserType"] = browserType
        });

        chunkedLogger?.LogMilestone(EventCodes.Worker.BrowserStartupRequested,
            "[sidecar] {browser}: browser startup requested", browserType);

        var psi = new ProcessStartInfo
        {
            FileName = options.NodeExe,
            Arguments = $"{options.SidecarScript} {browserType.ToLowerInvariant()}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        // Redact secrets if any in arguments (none currently but good practice)
        chunkedLogger?.LogInformation(null, "[sidecar] {browser}: starting process {FileName} {Arguments}",
            browserType, psi.FileName, psi.Arguments);

        // Optional: enable Playwright server-side debug logging for the Node sidecar.
        // Set PLAYWRIGHT_SERVER_DEBUG to a DEBUG namespace string. Examples:
        //   PLAYWRIGHT_SERVER_DEBUG=pw:server,pw:protocol
        //   PLAYWRIGHT_SERVER_DEBUG=1   (shorthand -> pw:server,pw:protocol)
        try
        {
            var dbg = Environment.GetEnvironmentVariable("PLAYWRIGHT_SERVER_DEBUG");
            if (!string.IsNullOrWhiteSpace(dbg))
            {
                var val = dbg.Trim();
                if (val == "1")
                {
                    val = "pw:server,pw:protocol";
                }

                // Ensure we don't clobber an existing DEBUG setting unintentionally
                if (!psi.Environment.TryAdd("DEBUG", val))
                {
                    psi.Environment["DEBUG"] = string.Join(",",
                        new[] { psi.Environment["DEBUG"], val }.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
            }

            // Log relevant environment variables
            var envOverrides = new Dictionary<string, string>();
            foreach (string key in psi.Environment.Keys)
            {
                if (key.StartsWith("PLAYWRIGHT_", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("DEBUG", StringComparison.OrdinalIgnoreCase))
                {
                    envOverrides[key] = psi.Environment[key] ?? "";
                }
            }

            if (envOverrides.Count > 0)
            {
                chunkedLogger?.LogInformation(null, "[sidecar] {browser}: environment overrides: {env}",
                    browserType, JsonSerializer.Serialize(envOverrides));
            }
        }
        catch
        {
            /* ignore env setup issues */
        }

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                chunkedLogger?.LogWarning(null, null, "[sidecar] {browser} [stderr]: {line}", browserType, e.Data);
            }
        };

        if (!proc.Start())
        {
            var ex = new InvalidOperationException("Failed to start Node sidecar process.");
            chunkedLogger?.LogError(ex, EventCodes.Worker.BrowserStartupFailed, "[sidecar] {browser}: failed to start process", browserType);
            op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Playwright);
            throw ex;
        }

        chunkedLogger?.LogMilestone(EventCodes.Worker.PlaywrightLaunched, "[sidecar] {browser}: process started (pid={pid})", browserType, proc.Id);

        proc.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.SidecarReadyTimeoutSeconds));
        string? payload = null;
        try
        {
            chunkedLogger?.LogDebug(null, "[sidecar] {browser}: waiting for ready signal...", browserType);
            while (true)
            {
                var ln = await proc.StandardOutput.ReadLineAsync(cts.Token);
                if (ln is null)
                {
                    // Process ended or stdout closed before emitting JSON
                    var exitCode = proc.HasExited ? proc.ExitCode : -1;
                    chunkedLogger?.LogWarning(EventCodes.Worker.SidecarExited, "[sidecar] {browser}: stdout closed before ready signal (exitCode={exitCode})", browserType, exitCode);
                    throw new TimeoutException($"Sidecar exited before providing wsEndpoint (exitCode={exitCode}).");
                }

                if (string.IsNullOrWhiteSpace(ln))
                {
                    chunkedLogger?.LogDebug(null, "[sidecar] {browser}: received empty line", browserType);
                    continue;
                }

                chunkedLogger?.LogDebug(null, "[sidecar] {browser} [stdout]: {line}", browserType, ln);

                try
                {
                    var probe = JsonNode.Parse(ln)?.AsObject();
                    if (probe is not null && probe.ContainsKey("wsEndpoint"))
                    {
                        payload = ln;
                        chunkedLogger?.LogMilestone(EventCodes.Worker.SidecarReady, "[sidecar] {browser}: received ready signal: {payload}", browserType, payload);
                        break;
                    }
                }
                catch
                {
                    // Ignore non-JSON or unrelated lines from the sidecar
                }
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            var ex = new TimeoutException($"Timed out waiting for sidecar wsEndpoint after {options.SidecarReadyTimeoutSeconds}s.");
            chunkedLogger?.LogError(ex, EventCodes.Worker.BrowserStartupFailed, "[sidecar] {browser}: startup timed out", browserType);
            op?.Fail(ex, ErrorType.Timeout, DependencyName.Playwright);
            throw ex;
        }
        catch (Exception ex)
        {
            TryKill(proc);
            chunkedLogger?.LogError(ex, EventCodes.Worker.BrowserStartupFailed, "[sidecar] {browser}: startup failed: {message}", browserType, ex.Message);
            op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Playwright);
            throw;
        }

        try
        {
            if (JsonNode.Parse(payload) is not JsonObject obj)
            {
                throw new InvalidOperationException("Sidecar output was not a JSON object.");
            }

            var wsNode = obj["wsEndpoint"];
            if (wsNode is null)
            {
                throw new InvalidOperationException("Sidecar JSON missing wsEndpoint.");
            }

            var wsEndpoint = wsNode.GetValue<string>();
            var pwVersion = obj["playwrightVersion"]?.GetValue<string>();
            var browserVersion = obj["browserVersion"]?.GetValue<string>();
            var browser = obj["browser"]?.GetValue<string>() ?? browserType.ToLowerInvariant();

            chunkedLogger?.LogMilestone(EventCodes.Worker.BrowserConnected,
                "[sidecar] {browser}: startup completed. ws={ws}, pw={pw}, browserVer={browserVer}",
                browser, wsEndpoint, pwVersion ?? "unknown", browserVersion ?? "unknown");

            op?.Complete();

            return new SidecarStartResult(proc, wsEndpoint, pwVersion, browserVersion, browser);
        }
        catch (Exception ex)
        {
            TryKill(proc);
            chunkedLogger?.LogError(ex, EventCodes.Worker.BrowserStartupFailed, "[sidecar] {browser}: failed to parse ready signal: {message}", browserType, ex.Message);
            op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Playwright);
            throw;
        }
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(true);
            }
        }
        catch { }
    }
}
