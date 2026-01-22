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

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Dashboard;
using Dashboard.Application;
using Dashboard.Application.Ports;
using Dashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR.Client;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

const string hubSignalRConfigKey = "AGENIX_DASHBOARD_HUB_SIGNALR_URL";

var builder = WebApplication.CreateBuilder(args);

// Ensure only Serilog is used for logging to prevent duplication and interleaving
builder.Logging.ClearProviders();

// Configure Serilog from appsettings.json
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

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
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// Per-user avatar state for sidebar/profile synchronization
builder.Services.AddScoped<AvatarState>();
// In-circuit toast notifications service
builder.Services.AddScoped<ToastService>();
// Centralized HTTP error handling
builder.Services.AddScoped<IHttpErrorHandler, HttpErrorHandler>();
// Request deduplication to prevent redundant in-flight API calls
builder.Services.AddScoped<IRequestDeduplicationService, RequestDeduplicationService>();

// Persist DataProtection keys to avoid antiforgery/cookie invalidation across restarts/containers
// Be resilient to permission issues inside containers by probing writability and falling back to a temp path.
try
{
    static bool CanWriteToDirectory(string path)
    {
        try
        {
            var testFile = Path.Combine(path, $".dp-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(testFile)) { }

            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    string? resolvedDpPath = null;
    var configuredPath = builder.Configuration["DASHBOARD_DP_KEYS"] ?? "/data/protection";

    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        try
        {
            Directory.CreateDirectory(configuredPath);
            if (CanWriteToDirectory(configuredPath))
            {
                resolvedDpPath = configuredPath;
            }
        }
        catch
        {
            /* ignore and try fallback */
        }
    }

    if (resolvedDpPath is null)
    {
        var fallback = Path.Combine(Path.GetTempPath(), "playwright-dashboard-dp-keys");
        try
        {
            Directory.CreateDirectory(fallback);
            if (CanWriteToDirectory(fallback))
            {
                resolvedDpPath = fallback;
            }
        }
        catch
        {
            /* as a last resort, skip persistence */
        }
    }

    if (resolvedDpPath is not null)
    {
        builder.Services.AddDataProtection()
            .SetApplicationName("playwright-dashboard")
            .PersistKeysToFileSystem(new DirectoryInfo(resolvedDpPath));
        Console.WriteLine($"[dashboard] Using DataProtection key directory: {resolvedDpPath}");
    }
    else
    {
        // Not fatal: fall back to default ephemeral behavior; cookies may invalidate on restart.
        Console.WriteLine(
            "[dashboard] Warning: No writable path for DataProtection keys. Falling back to default (non-persisted) keys.");
    }
}
catch
{
    // Non-fatal in dev/test; without persistence, old antiforgery cookies might fail after restart
}

// Authentication (Cookie always; OIDC optionally)
var oidcAuthority = builder.Configuration["OIDC_AUTHORITY"];
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = string.IsNullOrWhiteSpace(oidcAuthority)
        ? CookieAuthenticationDefaults.AuthenticationScheme
        : OpenIdConnectDefaults.AuthenticationScheme;
});

authBuilder.AddCookie(options =>
{
    options.SlidingExpiration = true;
    // Dynamic session timeout - will be updated from settings
    options.ExpireTimeSpan = TimeSpan.FromHours(24); // Default 24 hours
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Dynamic timeout validation on each request
    options.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            // If no authenticated principal, check for remember-me token
            if (context.Principal?.Identity?.IsAuthenticated != true)
            {
                if (context.HttpContext.Request.Cookies.TryGetValue("RememberMe", out var rememberMeToken) &&
                    !string.IsNullOrWhiteSpace(rememberMeToken))
                {
                    try
                    {
                        // Validate remember-me token via hub
                        var httpClientFactory =
                            context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
                        var hubClient = httpClientFactory.CreateClient(HttpClientNames.Hub);
                        var tokenHash = RememberMeHelper.HashToken(rememberMeToken);
                        var response =
                            await hubClient.GetAsync(
                                $"/admin/auth/remember-me?tokenHash={Uri.EscapeDataString(tokenHash)}");

                        if (response.IsSuccessStatusCode)
                        {
                            var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                            if (payload != null)
                            {
                                var userId =
                                    payload.TryGetValue("userId", out var uidEl) &&
                                    uidEl.ValueKind == JsonValueKind.String
                                        ? uidEl.GetString()
                                        : null;
                                var username =
                                    payload.TryGetValue("username", out var unEl) &&
                                    unEl.ValueKind == JsonValueKind.String
                                        ? unEl.GetString()
                                        : null;
                                var role = payload.TryGetValue("role", out var roleEl) &&
                                           roleEl.ValueKind == JsonValueKind.String
                                    ? roleEl.GetString()
                                    : null;

                                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username))
                                {
                                    // Create new session from remember-me token
                                    var claims = new List<Claim>
                                    {
                                        new(ClaimTypes.NameIdentifier, username),
                                        new(ClaimTypes.Name, username),
                                        new("preferred_username", username)
                                    };
                                    if (!string.IsNullOrEmpty(role))
                                    {
                                        claims.Add(new Claim(ClaimTypes.Role, role));
                                        claims.Add(new Claim("role", role));
                                    }

                                    var identity = new ClaimsIdentity(claims,
                                        CookieAuthenticationDefaults.AuthenticationScheme);
                                    var principal = new ClaimsPrincipal(identity);

                                    // Sign in with new session
                                    await context.HttpContext.SignInAsync(
                                        CookieAuthenticationDefaults.AuthenticationScheme, principal);

                                    // Update context
                                    context.ReplacePrincipal(principal);
                                    context.ShouldRenew = true;

                                    return;
                                }
                            }
                        }

                        // Invalid or expired token - clear the cookie
                        context.HttpContext.Response.Cookies.Delete("RememberMe");
                    }
                    catch
                    {
                        // Failed to validate - clear the cookie
                        context.HttpContext.Response.Cookies.Delete("RememberMe");
                    }
                }

                return;
            }

            try
            {
                // Fetch current timeout setting from hub
                var httpClientFactory = context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
                var hubClient = httpClientFactory.CreateClient(HttpClientNames.Hub);
                var response = await hubClient.GetAsync("/admin/settings");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("sessionTimeoutMinutes", out var timeoutEl))
                    {
                        var timeoutMinutes = timeoutEl.GetInt32();
                        var timeoutSpan = TimeSpan.FromMinutes(timeoutMinutes);

                        // With SlidingExpiration, check if the session has expired based on ExpiresUtc
                        // ExpiresUtc gets updated automatically on each request when sliding expiration is enabled
                        if (context.Properties.ExpiresUtc.HasValue)
                        {
                            // Check if session has expired
                            if (DateTimeOffset.UtcNow >= context.Properties.ExpiresUtc.Value)
                            {
                                context.RejectPrincipal();
                                await context.HttpContext.SignOutAsync(
                                    CookieAuthenticationDefaults.AuthenticationScheme);

                                // Check if remember-me cookie exists to auto-renew
                                if (context.HttpContext.Request.Cookies.ContainsKey("RememberMe"))
                                {
                                    // Will be handled on next request
                                }

                                return;
                            }
                        }

                        // Update the expiration based on the current timeout setting
                        // This allows dynamic timeout changes to take effect
                        var issuedUtc = context.Properties.IssuedUtc ?? DateTimeOffset.UtcNow;
                        context.Properties.ExpiresUtc = DateTimeOffset.UtcNow + timeoutSpan;

                        // Important: Mark for renewal so the cookie gets reissued with the new expiration
                        context.ShouldRenew = true;
                    }
                }
            }
            catch
            {
                // If settings fetch fails, use default behavior
            }
        }
    };
});

if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    authBuilder.AddOpenIdConnect(options =>
    {
        options.Authority = oidcAuthority;
        options.ClientId = builder.Configuration["OIDC_CLIENT_ID"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["OIDC_CLIENT_SECRET"];
        options.ResponseType = builder.Configuration["OIDC_RESPONSE_TYPE"] ?? "code";
        var scopes = builder.Configuration["OIDC_SCOPES"];
        if (!string.IsNullOrWhiteSpace(scopes))
        {
            foreach (var s in scopes.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                options.Scope.Add(s);
            }
        }

        options.CallbackPath = builder.Configuration["OIDC_CALLBACK_PATH"] ?? "/signin-oidc";
        options.SignedOutCallbackPath = builder.Configuration["OIDC_SIGNOUT_CALLBACK_PATH"] ?? "/signout-callback-oidc";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });
}

// Optional: better network efficiency for Blazor Server updates
builder.Services.AddResponseCompression(options =>
{
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream"]);
});

// HttpClient targeting hub HTTP endpoints derived from HUB_SIGNALR
builder.Services.AddHttpClient(HttpClientNames.Hub, (sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("dashboard");

    string? httpBase = null;
    var hubSignalR = cfg[hubSignalRConfigKey];
    if (!string.IsNullOrWhiteSpace(hubSignalR) && Uri.TryCreate(hubSignalR, UriKind.Absolute, out var wsUri))
    {
        // Convert ws/wss to http/https and strip trailing /ws
        var scheme = wsUri.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsUri.Scheme
        };
        httpBase = $"{scheme}://{wsUri.Authority}";
    }
    else
    {
        var hubUrl = cfg["HUB_URL"];
        if (!string.IsNullOrWhiteSpace(hubUrl) && Uri.TryCreate(hubUrl, UriKind.Absolute, out var httpUri))
        {
            httpBase = $"{httpUri.Scheme}://{httpUri.Authority}";
        }
    }

    if (string.IsNullOrWhiteSpace(httpBase))
    {
        logger.LogWarning("Invalid HUB url(s) for http derivation; using default http base http://hub:5000");
        httpBase = "http://hub:5000";
    }

    client.BaseAddress = new Uri(httpBase);

    // Forward current authenticated user identity for RBAC (used by Hub to validate Global Admin)
    try
    {
        var httpCtx = sp.GetService<IHttpContextAccessor>()?.HttpContext;
        var principal = httpCtx?.User;
        string? uid = null;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            uid = principal.FindFirst("preferred_username")?.Value
                  ?? principal.FindFirst(ClaimTypes.Email)?.Value
                  ?? principal.Identity?.Name
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        client.DefaultRequestHeaders.Remove("x-user-id");
        if (!string.IsNullOrWhiteSpace(uid))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-user-id", uid);
        }
    }
    catch { }
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

// Auth middleware (no-op if OIDC not configured)
app.UseAuthentication();
app.UseAuthorization();

// Optional simple health endpoint
app.MapGet("/health", () => Results.Ok(new { ok = true }));

// Auth endpoints (login/logout)
app.MapGet("/auth/login", (HttpContext ctx, string? returnUrl, bool? rememberMe) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var authority = cfg["OIDC_AUTHORITY"];
    var redirectLogin = "/login";

    if (string.IsNullOrWhiteSpace(authority))
    {
        var msg = Uri.EscapeDataString("OIDC is not configured. Please contact your administrator.");
        var ru = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl!;
        return Results.Redirect($"{redirectLogin}?error={msg}&returnUrl={Uri.EscapeDataString(ru)}");
    }

    // Get username from claims to redirect to user's default project
    var username = ctx.User?.FindFirst("preferred_username")?.Value
                   ?? ctx.User?.Identity?.Name
                   ?? ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var defaultRedirect = !string.IsNullOrWhiteSpace(username) ? $"/{username}_default/launches/" : "/";
    var redirect = string.IsNullOrWhiteSpace(returnUrl) ? defaultRedirect : returnUrl!;
    var props = new AuthenticationProperties { RedirectUri = redirect, IsPersistent = rememberMe == true };
    if (props.IsPersistent)
    {
        props.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
    }

    return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapGet("/auth/logout", async (HttpContext ctx, string? returnUrl) =>
{
    // Clear remember-me token if present
    if (ctx.Request.Cookies.TryGetValue("RememberMe", out var rememberMeToken) &&
        !string.IsNullOrWhiteSpace(rememberMeToken))
    {
        try
        {
            var factory = ctx.RequestServices.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient(HttpClientNames.Hub);
            var tokenHash = RememberMeHelper.HashToken(rememberMeToken);
            await client.DeleteAsync($"/admin/auth/remember-me?tokenHash={Uri.EscapeDataString(tokenHash)}");
        }
        catch
        {
            // Non-fatal
        }

        // Clear the cookie
        ctx.Response.Cookies.Delete("RememberMe");
    }

    // Default to login after logout unless caller explicitly provides a different returnUrl
    var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/login" : returnUrl!;
    var props = new AuthenticationProperties { RedirectUri = redirect };

    // Sign out only from the schemes that are actually configured
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var schemes = new List<string> { CookieAuthenticationDefaults.AuthenticationScheme };
    if (!string.IsNullOrWhiteSpace(cfg["OIDC_AUTHORITY"]))
    {
        schemes.Add(OpenIdConnectDefaults.AuthenticationScheme);
    }

    return Results.SignOut(props, schemes.ToArray());
});

// Local auth (enabled when OIDC is not configured)
app.MapPost("/auth/local", async (HttpContext ctx) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var authority = cfg["OIDC_AUTHORITY"];
    if (!string.IsNullOrWhiteSpace(authority))
    {
        return Results.BadRequest(new { error = "Local login is disabled when OIDC is configured." });
    }

    Dictionary<string, JsonElement>? doc;
    try { doc = await ctx.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
    catch { return Results.BadRequest(new { error = "invalid JSON" }); }

    var username = doc != null && doc.TryGetValue("username", out var uEl) && uEl.ValueKind == JsonValueKind.String
        ? (uEl.GetString() ?? string.Empty).Trim()
        : string.Empty;
    var password = doc != null && doc.TryGetValue("password", out var pEl) && pEl.ValueKind == JsonValueKind.String
        ? pEl.GetString() ?? string.Empty
        : string.Empty;
    var rememberMe = doc != null && doc.TryGetValue("rememberMe", out var rEl) && rEl.ValueKind == JsonValueKind.True
        ? true
        : doc != null && doc.TryGetValue("rememberMe", out rEl) && rEl.ValueKind == JsonValueKind.False
            ? false
            : false;
    var returnUrl = doc != null && doc.TryGetValue("returnUrl", out var ruEl) && ruEl.ValueKind == JsonValueKind.String
        ? ruEl.GetString() ?? string.Empty
        : string.Empty;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
    {
        return Results.BadRequest(new { error = "Username and password are required" });
    }

    // Authenticate with Hub
    try
    {
        var factory = ctx.RequestServices.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(HttpClientNames.Hub);
        using var resp = await client.PostAsJsonAsync("/admin/auth/login", new { id = username, username, password });
        if (!resp.IsSuccessStatusCode)
        {
            return Results.Unauthorized();
        }

        var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var id = payload != null && payload.TryGetValue("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? username
            : username;
        var display =
            payload != null && payload.TryGetValue("username", out var dnEl) && dnEl.ValueKind == JsonValueKind.String
                ? dnEl.GetString() ?? id
                : id;
        var role = payload != null && payload.TryGetValue("role", out var roleEl) &&
                   roleEl.ValueKind == JsonValueKind.String
            ? roleEl.GetString() ?? ""
            : "";

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, display) };
        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        // Preferred username for x-user-id forwarding
        claims.Add(new Claim("preferred_username", id));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Redirect to user's default project launches page
        var defaultRedirect = $"/{id}_default/launches/";
        var finalRedirect = string.IsNullOrWhiteSpace(returnUrl) ? defaultRedirect : returnUrl;

        var props = new AuthenticationProperties
        {
            IsPersistent = false, // Session cookie only, remember-me handled separately
            RedirectUri = finalRedirect
        };

        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

        // Create remember-me token if requested
        if (rememberMe)
        {
            try
            {
                // Use 'id' from the login response as userId
                if (!string.IsNullOrEmpty(id))
                {
                    var token = RememberMeHelper.GenerateToken();
                    var tokenHash = RememberMeHelper.HashToken(token);

                    // Store token in database via hub
                    var createTokenResp = await client.PostAsJsonAsync("/admin/auth/remember-me",
                        new { userId = id, tokenHash, expiresUtc = DateTime.UtcNow.AddDays(90) });

                    if (createTokenResp.IsSuccessStatusCode)
                    {
                        // Set remember-me cookie (90 days, HttpOnly, Secure)
                        ctx.Response.Cookies.Append("RememberMe", token,
                            new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.Strict,
                                Expires = DateTimeOffset.UtcNow.AddDays(90)
                            });
                    }
                }
            }
            catch
            {
                // Non-fatal: session still works without remember-me
            }
        }

        return Results.Ok(new { ok = true, redirect = props.RedirectUri });
    }
    catch (Exception ex)
    {
        try
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("dashboard")
                .LogWarning(ex, "Local auth failed");
        }
        catch { }

        return Results.Unauthorized();
    }
});

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
        if (string.IsNullOrEmpty(v))
        {
            return v ?? string.Empty;
        }

        return v!.Length <= max ? v : v.Substring(0, max);
    }

    var flags = app.Services.GetRequiredService<DashboardFeatureFlags>();
    var hubUrl = app.Configuration[hubSignalRConfigKey] ?? "http://hub:5000/ws";

    var diag = new
    {
        Version = TruncVer(GetInformationalVersion()),
        HubSignalR = hubUrl,
        OpenTelemetry =
            new
            {
                EnableOtlp = enableOtlp,
                EnablePrometheusOtel = enablePromOtel,
                OtlpEndpoint = otlpEndpoint,
                OtlpProtocol = otlpProtocol
            },
        Features = new { flags.FiltersEnabled, flags.VirtualizationEnabled, flags.LiveFeedEnabled }
    };

    var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
    app.Logger.LogInformation("[dashboard] Startup diagnostics:\n{json}", json);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "[dashboard] Startup diagnostics failed");
}

// Artifact proxy endpoint - forwards requests to Hub API
app.MapGet("/api/artifacts/{id:guid}", async (Guid id, IHttpClientFactory factory, HttpContext context) =>
{
    var hubClient = factory.CreateClient(HttpClientNames.Hub);
    var inline = context.Request.Query["inline"].FirstOrDefault() == "true";
    var url = $"/api/artifacts/{id}?inline={inline}";

    try
    {
        var response = await hubClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var content = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var contentDisposition = response.Content.Headers.ContentDisposition?.ToString();

        return Results.File(content, contentType, null, true);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to proxy artifact request for {ArtifactId}", id);
        return Results.Problem("Failed to fetch artifact from Hub");
    }
});

// Test item artifacts zip download proxy
app.MapGet("/api/test-items/{id:guid}/artifacts/download-zip", async (Guid id, IHttpClientFactory factory) =>
{
    var hubClient = factory.CreateClient(HttpClientNames.Hub);
    var url = $"/api/test-items/{id}/artifacts/download-zip";

    try
    {
        var response = await hubClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var content = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/zip";
        var contentDisposition = response.Content.Headers.ContentDisposition?.ToString();

        return Results.File(content, contentType, null, true);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to proxy zip download request for test item {TestItemId}", id);
        return Results.Problem("Failed to download artifacts zip from Hub");
    }
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

await app.RunAsync();
