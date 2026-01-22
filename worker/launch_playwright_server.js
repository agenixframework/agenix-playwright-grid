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
        CHROME_ARGS=... (alias for CHROMIUM_ARGS if CHROMIUM_ARGS not set)
        WEBKIT_ARGS="--flag1 --flag2" or JSON array
        FIREFOX_ARGS="--flag1 --flag2" (optional; limited effect for Firefox)
        FIREFOX_PREFS as JSON object or key=value pairs (comma/semicolon/newline separated)
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
    } catch {
    }
    return {mod, pkgName, installedVersion};
  } catch (e) {
    // Fallback to default 'playwright' if custom package not found
    const fallbackName = 'playwright';
    const mod = require(fallbackName);
    let installedVersion = process.env.PLAYWRIGHT_VERSION || undefined;
    try {
      const pkg = require(`${fallbackName}/package.json`);
      installedVersion = installedVersion || pkg.version;
    } catch {
    }
    return {mod, pkgName: fallbackName, installedVersion};
  }
}

function parseArgs(envValue) {
  if (!envValue) return [];
  // Try JSON array first
  try {
    const parsed = JSON.parse(envValue);
    if (Array.isArray(parsed)) return parsed.map(x => String(x));
  } catch {
  }
  // Fallback: split by commas or whitespace
  return envValue
    .split(/[\s,]+/)
    .map(s => s.trim())
    .filter(Boolean);
}

function parsePrefs(envValue) {
  if (!envValue) return undefined;
  // Try JSON object first
  try {
    const obj = JSON.parse(envValue);
    if (obj && typeof obj === 'object' && !Array.isArray(obj)) return obj;
  } catch {
  }
  // Fallback: key=value pairs separated by comma/semicolon/newlines
  const map = {};
  const parts = String(envValue).split(/[\r\n;,]+/).map(s => s.trim()).filter(Boolean);
  for (const p of parts) {
    const eq = p.indexOf('=');
    if (eq <= 0) {
      console.error(`[sidecar] Ignoring malformed FIREFOX_PREFS entry: ${p}`);
      continue;
    }
    const key = p.substring(0, eq).trim();
    let val = p.substring(eq + 1).trim();
    if (!key || /\s/.test(key)) {
      console.error(`[sidecar] Ignoring FIREFOX_PREFS key with whitespace or empty: ${p}`);
      continue;
    }
    // Try to coerce to boolean/number when obvious
    if (/^(true|false)$/i.test(val)) {
      map[key] = /^true$/i.test(val);
    } else if (/^-?\d+(\.\d+)?$/.test(val)) {
      map[key] = Number(val);
    } else if ((val.startsWith('"') && val.endsWith('"')) || (val.startsWith("'") && val.endsWith("'"))) {
      map[key] = val.slice(1, -1);
    } else {
      map[key] = val;
    }
  }
  return Object.keys(map).length ? map : undefined;
}

(async () => {
  const {mod: pw, pkgName, installedVersion} = loadPlaywright();
  const {chromium, firefox, webkit} = pw;

  const name = (process.argv[2] || process.env.BROWSER || 'chromium').toLowerCase();
  const types = {chromium, firefox, webkit};
  const browserType = types[name];
  if (!browserType) {
    console.error(`Unknown browser: ${name} (expected chromium|firefox|webkit)`);
    process.exit(1);
  }

  // Resolve args per browser; allow CHROME_ARGS as alias for Chromium
  const chromiumArgsEnv = process.env.CHROMIUM_ARGS || process.env.CHROME_ARGS || '';
  const webkitArgsEnv = process.env.WEBKIT_ARGS || '';
  const firefoxArgsEnv = process.env.FIREFOX_ARGS || '';

  const chromiumArgs = parseArgs(chromiumArgsEnv);
  const webkitArgs = parseArgs(webkitArgsEnv);
  const firefoxArgs = parseArgs(firefoxArgsEnv);

  const args = name === 'chromium' ? chromiumArgs : name === 'webkit' ? webkitArgs : firefoxArgs;

  // Firefox user prefs
  const firefoxPrefs = name === 'firefox' ? parsePrefs(process.env.FIREFOX_PREFS) : undefined;

  const launchOptions = {headless: true};
  if (args && args.length) launchOptions.args = args;
  if (name === 'firefox' && firefoxPrefs) launchOptions.firefoxUserPrefs = firefoxPrefs;

  const server = await browserType.launchServer(launchOptions);

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
  } catch {
  }

  // Emit a single JSON line, the C# worker reads this line.
  console.log(JSON.stringify({
    wsEndpoint,
    browser: name,
    playwrightVersion: installedVersion,
    playwrightPackage: pkgName,
    browserVersion
  }));

  // Keep the process alive; close server on termination.
  const shutdown = async () => {
    try {
      await server.close();
    } catch {
    }
    process.exit(0);
  };
  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);

  process.stdin.resume();
})().catch(err => {
  console.error(err?.stack || String(err));
  process.exit(1);
});
