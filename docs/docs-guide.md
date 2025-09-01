# Documentation Guide

This repository keeps user- and developer-facing documentation under the docs/ folder. Use this as the primary place to document behavior, flows, and feature specifics. The root README.md remains a quickstart and high-level reference; link more detailed topics from there.

Where to update docs
- Test Client usage (HubClient in test runners): edit docs/TestClient-Usage.md.
- Test Results Dashboard and how Playwright API/protocol logs appear under the "Tests" tab: edit docs/TestResultsDashboard-Approach.md.
- HTTP API changes (new endpoints, parameters, headers): update the HTTP API summary in the root README.md and add or update dedicated docs pages under docs/ if more detail is needed.
- Configuration (env vars for Hub/Worker/Dashboard): keep root README.md authoritative and add deeper explanations under docs/ when necessary.

Related pointers
- Runner-side forwarding of API/protocol logs: use Agenix.PlaywrightGrid.HubClient HubClient.SendApiLogAsync / SendApiLogsAsync to POST to /results/browser/{browserId}/api-logs (requires HUB_RUNNER_SECRET).
- Worker-side Playwright protocol mirroring: workers proxy the browser WebSocket and mirror protocol messages to POST /results/browser/{browserId}/commands (requires HUB_NODE_SECRET).
- Test attribution for grouping under the Tests tab: call HubClient.SetCurrentTestAsync so logs are attributed to a TestId.

Contributing to docs
- Keep examples minimal and runnable where possible.
- When changing endpoints or secrets, update both README.md and the relevant docs/ page in the same PR.
- Prefer adding small, focused docs/ pages and link them from README.md sections.
