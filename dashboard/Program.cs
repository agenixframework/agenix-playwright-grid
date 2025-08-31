using System;
using System.Linq;
using Dashboard;
using Dashboard.Application.Ports;
using Dashboard.Infrastructure.Adapters.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string hubSignalRConfigKey = "HUB_SIGNALR";

var builder = WebApplication.CreateBuilder(args);

// Reduce noisy framework info logs in Dashboard (e.g., /health pipeline messages)
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// OpenTelemetry setup (env-driven exporters)
var dashboardServiceName = "playwright-dashboard";
var dashboardServiceVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
var enableOtlp = string.Equals(builder.Configuration["ENABLE_OTLP"], "1", StringComparison.OrdinalIgnoreCase);
var enablePromOtel = string.Equals(builder.Configuration["ENABLE_PROMETHEUS_OTEL"], "1", StringComparison.OrdinalIgnoreCase);
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc";

var resourceBuilder = OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
    .AddService(serviceName: dashboardServiceName, serviceVersion: dashboardServiceVersion);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService(serviceName: dashboardServiceName, serviceVersion: dashboardServiceVersion))
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
                    ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
                    : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
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
                    ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
                    : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
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

builder.Services.AddHostedService<SignalRClientService>();

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


app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

await app.RunAsync();
