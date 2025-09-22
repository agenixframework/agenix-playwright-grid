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

using System.Reflection;
using System.Text.Json;
using Dashboard;
using Dashboard.Application.Ports;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR.Client;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string hubSignalRConfigKey = "HUB_SIGNALR";

var builder = WebApplication.CreateBuilder(args);

// Reduce noisy framework info logs in Dashboard (e.g., /health pipeline messages)
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
// Reduce noisy HttpClient handler INFO logs (e.g., Received HTTP response headers ... - 200)
// Apply a coarse filter for all HttpClient logs and a specific override for our named client "hub".
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.hub", LogLevel.Warning);

// Apply environment-driven log levels (global + per-category overrides)
LoggingConfigurator.ApplyFromEnvironment(builder.Logging, builder.Configuration);

// OpenTelemetry setup (env-driven exporters)
var dashboardServiceName = "playwright-dashboard";
var dashboardServiceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
var enableOtlp = string.Equals(builder.Configuration["ENABLE_OTLP"], "1", StringComparison.OrdinalIgnoreCase);
var enablePromOtel =
    string.Equals(builder.Configuration["ENABLE_PROMETHEUS_OTEL"], "1", StringComparison.OrdinalIgnoreCase);
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc";

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(dashboardServiceName, serviceVersion: dashboardServiceVersion);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService(dashboardServiceName, serviceVersion: dashboardServiceVersion))
    .WithTracing(t =>
    {
        t.SetResourceBuilder(resourceBuilder);
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        if (enableOtlp)
        {
            t.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc;
            });
        }
    })
    .WithMetrics(m =>
    {
        m.SetResourceBuilder(resourceBuilder);
        m.AddAspNetCoreInstrumentation();
        m.AddRuntimeInstrumentation();
        if (enableOtlp)
        {
            m.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc;
            });
        }
    });


// Services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Optional: better network efficiency for Blazor Server updates
builder.Services.AddResponseCompression(options =>
{
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream"]);
});

// HttpClient targeting hub HTTP endpoints derived from HUB_SIGNALR
builder.Services.AddHttpClient("hub", (sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("dashboard");
    var hubUrl = cfg[hubSignalRConfigKey] ?? "http://hub:5000/ws";
    if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var uri))
    {
        logger.LogWarning("Invalid HUB url for http derivation; using default http base http://hub:5000");
        client.BaseAddress = new Uri("http://hub:5000");
        return;
    }

    // Convert ws/wss to http/https and strip trailing /ws
    var scheme = uri.Scheme switch
    {
        "ws" => "http",
        "wss" => "https",
        _ => uri.Scheme
    };
    var baseUri = new Uri($"{scheme}://{uri.Authority}");
    client.BaseAddress = baseUri;

    // Forward runner secret for admin actions against the hub
    var secret = cfg["HUB_RUNNER_SECRET"] ?? "runner-secret";
    client.DefaultRequestHeaders.Remove("x-hub-secret");
    client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", secret);
});

// HubConnection as a singleton configured from a configuration / environment
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("dashboard");
    var hubUrl = cfg[hubSignalRConfigKey] ?? "http://hub:5000/ws";

    if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ws" && uri.Scheme != "wss"))
    {
        logger.LogWarning("Invalid HUB url in configuration key '{Key}': '{Url}'. Falling back to default.",
            hubSignalRConfigKey, hubUrl);
        hubUrl = "http://hub:5000/ws";
    }

    return new HubConnectionBuilder()
        .WithUrl(hubUrl)
        .WithAutomaticReconnect()
        .Build();
});

// App state + background SignalR client
builder.Services.AddSingleton<PoolStateProxy>();
// Expose hexagonal ports to UI and adapters via the same state holder
builder.Services.AddSingleton<IPoolStateReader>(sp => sp.GetRequiredService<PoolStateProxy>());
builder.Services.AddSingleton<IPoolStateWriter>(sp => sp.GetRequiredService<PoolStateProxy>());

// Connection status state for SignalR UI feedback
builder.Services.AddSingleton<ConnectionStatusProxy>();
builder.Services.AddSingleton<IConnectionStatusReader>(sp => sp.GetRequiredService<ConnectionStatusProxy>());
builder.Services.AddSingleton<IConnectionStatusWriter>(sp => sp.GetRequiredService<ConnectionStatusProxy>());

builder.Services.AddHostedService<SignalRClientService>();

// Feature flags for Dashboard (env-driven)
builder.Services.AddSingleton(sp => DashboardFeatureFlags.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseResponseCompression();
app.UseStaticFiles();

// Optional simple health endpoint
app.MapGet("/health", () => Results.Ok(new { ok = true }));

// Startup diagnostics dump (effective dashboard config)
try
{
    static string GetInformationalVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var aiv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return aiv?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? string.Empty;
        }
        catch { return string.Empty; }
    }
    static string TruncVer(string? v)
    {
        const int max = 15; // "1.0.1-preview.3".Length
        if (string.IsNullOrEmpty(v)) return v ?? string.Empty;
        return v!.Length <= max ? v : v.Substring(0, max);
    }

    var flags = app.Services.GetRequiredService<DashboardFeatureFlags>();
    var hubUrl = app.Configuration[hubSignalRConfigKey] ?? "http://hub:5000/ws";

    var diag = new
    {
        Version = TruncVer(GetInformationalVersion()),
        HubSignalR = hubUrl,
        OpenTelemetry = new
        {
            EnableOtlp = enableOtlp,
            EnablePrometheusOtel = enablePromOtel,
            OtlpEndpoint = otlpEndpoint,
            OtlpProtocol = otlpProtocol
        },
        Features = new
        {
            flags.FiltersEnabled,
            flags.VirtualizationEnabled,
            flags.LiveFeedEnabled
        }
    };

    var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
    app.Logger.LogInformation("[dashboard] Startup diagnostics:\n{json}", json);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "[dashboard] Startup diagnostics failed");
}

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

await app.RunAsync();
