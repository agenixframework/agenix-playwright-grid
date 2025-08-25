/* 
  Launches a Playwright browser server and prints {"wsEndpoint":"ws://...","browser":"chromium","playwrightVersion":"x.y.z"} to stdout.
  Usage:
    node launch_playwright_server.js chromium|firefox|webkit
  Notes:
    - Keep this process running to keep the server alive.
    - On SIGTERM/SIGINT, it closes the server cleanly.
    - Configure via env:
        BROWSER=chromium|firefox|webkit
        CHROMIUM_ARGS="--flag1 --flag2" or "--flag1,--flag2" or JSON array ["--flag1","--flag2"]
        PLAYWRIGHT_PACKAGE=playwright (optional)
        PLAYWRIGHT_VERSION=1.54.2 (optional, reported; real version comes from installed package)
    - Interception model:
        The worker does NOT intercept here. It exposes a stable proxy WebSocket at /ws/{browserId}
        (WebServerHost.cs) and connects that to this server's wsEndpoint(). The proxy forwards frames
        and mirrors text protocol messages to the Hub (/results/browser/{browserId}/commands) so the
        dashboard can display the Playwright protocol traffic per run.
*/

function loadPlaywright() {
    const pkgName = process.env.PLAYWRIGHT_PACKAGE || 'playwright';
    try {
        // Try the requested package name first
        const mod = require(pkgName);
        let installedVersion = process.env.PLAYWRIGHT_VERSION || undefined;
        try {
            const pkg = require(`${pkgName}/package.json`);
            installedVersion = installedVersion || pkg.version;
        } catch {}
        return { mod, pkgName, installedVersion };
    } catch (e) {
        // Fallback to default 'playwright' if custom package not found
        const fallbackName = 'playwright';
        const mod = require(fallbackName);
        let installedVersion = process.env.PLAYWRIGHT_VERSION || undefined;
        try {
            const pkg = require(`${fallbackName}/package.json`);
            installedVersion = installedVersion || pkg.version;
        } catch {}
        return { mod, pkgName: fallbackName, installedVersion };
    }
}

function parseArgs(envValue) {
    if (!envValue) return [];
    // Try JSON array first
    try {
        const parsed = JSON.parse(envValue);
        if (Array.isArray(parsed)) return parsed.map(x => String(x));
    } catch {}
    // Fallback: split by commas or whitespace
    return envValue
        .split(/[\s,]+/)
        .map(s => s.trim())
        .filter(Boolean);
}

(async () => {
    const { mod: pw, pkgName, installedVersion } = loadPlaywright();
    const { chromium, firefox, webkit } = pw;

    const name = (process.argv[2] || process.env.BROWSER || 'chromium').toLowerCase();
    const types = { chromium, firefox, webkit };
    const browserType = types[name];
    if (!browserType) {
        console.error(`Unknown browser: ${name} (expected chromium|firefox|webkit)`);
        process.exit(1);
    }

    // Only pass Chromium-compatible flags to Chromium. For Firefox/WebKit, keep args empty.
    const chromiumArgs = parseArgs(process.env.CHROMIUM_ARGS);
    const args = name === 'chromium' ? chromiumArgs : [];

    const server = await browserType.launchServer({
        headless: true,
        args
    });

    const wsEndpoint = server.wsEndpoint();

    // Derive browser version by briefly connecting and reading version(), then close the client.
    let browserVersion = undefined;
    try {
        const client = await browserType.connect(wsEndpoint);
        try {
            browserVersion = client.version();
        } finally {
            await client.close();
        }
    } catch {}

    // Emit a single JSON line, the C# worker reads this line.
    console.log(JSON.stringify({ wsEndpoint, browser: name, playwrightVersion: installedVersion, playwrightPackage: pkgName, browserVersion }));

    // Keep the process alive; close server on termination.
    const shutdown = async () => {
        try {
            await server.close();
        } catch {}
        process.exit(0);
    };
    process.on('SIGTERM', shutdown);
    process.on('SIGINT', shutdown);

    process.stdin.resume();
})().catch(err => {
    console.error(err?.stack || String(err));
    process.exit(1);
});
