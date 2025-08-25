using System.Diagnostics;
using System.Text.Json.Nodes;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public interface ISidecarLauncher
{
    Task<SidecarStartResult> StartAsync(string browserType, CancellationToken ct = default);
}

public readonly record struct SidecarStartResult(Process proc, string ws, string? playwrightVersion, string? browserVersion, string browser);

public sealed class SidecarLauncher(WorkerOptions options) : ISidecarLauncher
{
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
                if (val == "1") val = "pw:server,pw:protocol";
                // Ensure we don't clobber an existing DEBUG setting unintentionally
                if (!psi.Environment.TryAdd("DEBUG", val))
                    psi.Environment["DEBUG"] = string.Join(",", new[] { psi.Environment["DEBUG"], val }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
        }
        catch { /* ignore env setup issues */ }

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[sidecar:{browserType}] {e.Data}");
        };

        if (!proc.Start())
            throw new InvalidOperationException("Failed to start Node sidecar process.");

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
                if (string.IsNullOrWhiteSpace(ln)) continue;
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
            var json = JsonNode.Parse(payload!).AsObject();
            var wsEndpoint = json["wsEndpoint"].GetValue<string>();
            var pwVersion = json["playwrightVersion"]?.GetValue<string>();
            var browserVersion = json["browserVersion"]?.GetValue<string>();
            var browser = json["browser"]?.GetValue<string>() ?? browserType.ToLowerInvariant();
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
        try { if (!proc.HasExited) proc.Kill(true); } catch { }
    }
}
