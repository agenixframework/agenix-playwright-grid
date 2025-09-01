# Playwright Grid

A lightweight, scalable grid for borrowing Playwright browser sessions over WebSocket. It includes:
- Hub (capacity broker + Redis)
- Workers (sidecar-managed Playwright servers + WS proxy)
- Dashboard (live runs, logs)
- Thin HubClient for test runners

Use the links below to get started and dive into specific topics.

## Start here
- Overview & Quick Start: docs-guide.md
- Test Runner Client Usage: TestClient-Usage.md
- UI / Dashboard Guide: ui-user-guide.md

## How it works
- Label Matching strategy: Label-Matching.md
- Capacity Queue (pending borrows, fairness, timeouts): Capacity-Queue.md
- Borrow TTL & Session Persistence: Borrow-TTL-and-Session-Persistence.md
- Node Liveness & Sweeper: Node-Liveness-and-Sweeper.md
- Session Distribution across workers: distribution.md

## Observability
- Metrics and Grafana dashboards: Metrics-and-Grafana.md

## Project & roadmap
- Improvement Tasks Checklist: tasks.md

Notes
- All links are relative so this site works when hosted under root (/).
