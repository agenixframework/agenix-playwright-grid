# API Reference

This project generates API documentation from XML comments in source code.

- Hub (ASP.NET Core Minimal API): Swagger UI is available when the Hub is running. The OpenAPI schema and operation descriptions are populated from XML documentation files.
  - Local (Docker Compose default): http://127.0.0.1:5100/swagger
  - Container internal: http://hub:5000/swagger
  - The Hub includes XML comments automatically during startup.

- HubClient (NuGet library): XML documentation is produced during build and packaged with the assembly for IDE Intellisense and external documentation tooling.

Notes
- All public APIs and DTOs include XML documentation and explicit nullable annotations.
- You can export OpenAPI JSON from the Hub at /swagger/v1/swagger.json for use with clients or external documentation sites.

HubClient notes
- ReturnAsync is obsolete and a no-op. Do not call it; the Hub auto-finishes/auto-returns sessions. Samples have been updated to reflect this.

Run finalization and AutoReturn semantics
- The Worker now issues POST /session/return to the Hub automatically when the client Playwright WebSocket disconnects. This ensures runs do not remain in the Running state after WS close.
- The /session/return endpoint is idempotent and, when a runId correlation is available, marks the run as:
  - Failed if any test failures were recorded; otherwise Passed.
  - Timestamps are updated and a Return command event is appended to the run log.
- AutoReturn refers to capacity being automatically released (either by the Worker on WS close or by a Hub sweeper). Operationally, a clean AutoReturn with no recorded failures is equivalent to a Passed run. The Dashboard may show an AutoReturn event in the command log while the run status is Passed.
- Separately, if a run is inactive for too long or exceeds max duration with outstanding browsers, the Hub may mark it AutoStopped; this is distinct from a clean Passed/Failed finalization.

# API Reference

This project generates API documentation from XML comments in source code.

- Hub (ASP.NET Core Minimal API): Swagger UI is available when the Hub is running. The OpenAPI schema and operation descriptions are populated from XML documentation files.
  - Local (Docker Compose default): http://127.0.0.1:5100/swagger
  - Container internal: http://hub:5000/swagger
  - The Hub includes XML comments automatically during startup.

- HubClient (NuGet library): XML documentation is produced during build and packaged with the assembly for IDE Intellisense and external documentation tooling.

Notes
- All public APIs and DTOs include XML documentation and explicit nullable annotations.
- You can export OpenAPI JSON from the Hub at /swagger/v1/swagger.json for use with clients or external documentation sites.

HubClient notes
- ReturnAsync is obsolete and a no-op. Do not call it; the Hub auto-finishes/auto-returns sessions. Samples have been updated to reflect this.

Run finalization and AutoReturn semantics
- The Worker now issues POST /session/return to the Hub automatically when the client Playwright WebSocket disconnects. This ensures runs do not remain in the Running state after WS close.
- The /session/return endpoint is idempotent and, when a runId correlation is available, marks the run as:
  - Failed if any test failures were recorded; otherwise Passed.
  - Timestamps are updated and a Return command event is appended to the run log.
- AutoReturn refers to capacity being automatically released (either by the Worker on WS close or by a Hub sweeper). Operationally, a clean AutoReturn with no recorded failures is equivalent to a Passed run. The Dashboard may show an AutoReturn event in the command log while the run status is Passed.
- Separately, if a run is inactive for too long or exceeds max duration with outstanding browsers, the Hub may mark it AutoStopped; this is distinct from a clean Passed/Failed finalization.

---

Borrow a session — POST /session/borrow

Headers
- x-hub-secret: <HUB_RUNNER_SECRET> (required)
- content-type: application/json
- Optional correlation: query runId=<uuid> and/or header Correlation-Id: <uuid>

Request body (JSON)
{
  "labelKey": "AppB:Chromium:UAT",
  "runName": "Smoke UAT #123"
}

Response 200 (JSON)
{
  "browserId": "a1b2c3d4",
  "webSocketEndpoint": "ws://127.0.0.1:5200/ws/a1b2c3d4",
  "browserType": "chromium"
}

Error responses
- 401 Unauthorized if the secret is missing or invalid
- 503 Service Unavailable when capacity is not available within the configured timeout
- 4xx for validation errors (invalid labelKey or runName)

RunName (optional)
- Trimmed; empty is treated as not supplied.
- Max length 128.
- Allowed chars: letters, digits, space, dot (.), underscore (_), hyphen (-).
- Control characters are not allowed.
- Case policy: casing is preserved; comparisons/filtering (e.g., Dashboard) are case-insensitive.
- May include descriptive text to help identify runs (e.g., "Smoke UAT #123").
- Security/PII: avoid including secrets or personal data. If you need to suppress RunName in hub logs, set HUB_REDACT_RUNNAME=1 (UI/storage continue to show the provided value).

Swagger (OpenAPI) snippet
- The Swagger UI shows runName as an optional string property on BorrowRequest.
- Example schema excerpt:
  BorrowRequest:
    type: object
    required: [ labelKey ]
    properties:
      labelKey:
        type: string
        example: "AppB:Chromium:UAT"
      runName:
        type: string
        nullable: true
        description: Optional human-friendly run name (<=128 chars; letters/numbers/space/._-)
        example: "Smoke UAT #123"

Curl example
curl -s -X POST http://127.0.0.1:5100/session/borrow \
  -H 'content-type: application/json' \
  -H 'x-hub-secret: runner-secret' \
  -d '{"labelKey":"AppB:Chromium:UAT","runName":"Smoke UAT #123"}'

Dashboard example
- Open the Dashboard pre-filtered by run name:
  http://127.0.0.1:3001/results?runName=Smoke%20UAT%20%23123
