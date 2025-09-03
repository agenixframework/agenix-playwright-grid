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

using System.Diagnostics;
using System.Text.Json.Nodes;
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

public sealed class SidecarLauncher(WorkerOptions options) : ISidecarLauncher
{
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = Microsoft.Extensions.Logging.LoggerFactory
        .Create(b => b.AddSimpleConsole())
        .CreateLogger("worker.sidecar");

    public async Task<SidecarStartResult> StartAsync(string browserType, CancellationToken ct = default)
    {
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
                Logger.LogWarning("[sidecar] {browser}: {line}", browserType, e.Data);
            }
        };

        if (!proc.Start())
        {
            throw new InvalidOperationException("Failed to start Node sidecar process.");
        }

        proc.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.SidecarReadyTimeoutSeconds));
        string? payload = null;
        try
        {
            while (true)
            {
                var ln = await proc.StandardOutput.ReadLineAsync(cts.Token);
                if (ln is null)
                {
                    // Process ended or stdout closed before emitting JSON
                    throw new TimeoutException("Sidecar exited before providing wsEndpoint.");
                }

                if (string.IsNullOrWhiteSpace(ln))
                {
                    continue;
                }

                try
                {
                    var probe = JsonNode.Parse(ln)?.AsObject();
                    if (probe is not null && probe.ContainsKey("wsEndpoint"))
                    {
                        payload = ln;
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
            throw new TimeoutException("Timed out waiting for sidecar wsEndpoint.");
        }

        try
        {
            var obj = JsonNode.Parse(payload!) as JsonObject;
            if (obj is null)
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
            return new SidecarStartResult(proc, wsEndpoint, pwVersion, browserVersion, browser);
        }
        catch
        {
            TryKill(proc);
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
