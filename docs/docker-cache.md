# Docker build cache strategy (BuildKit)

This repository’s Dockerfiles have been optimized to take advantage of Docker BuildKit persistent caches and improved layer ordering to speed up local and CI builds.

Key points
- BuildKit syntax enabled at the top of each Dockerfile: `# syntax=docker/dockerfile:1.4`.
- NuGet cache is persisted across builds using cache mounts during `dotnet restore`/`dotnet publish`.
- Worker image also persists npm and Playwright browser caches to avoid repeated downloads.
- Heavy, slow-changing steps (Node runtime, apt packages) are placed before copying application code to maximize cache hits.

What changed in Dockerfiles
- Hub/Dashboard/Worker build stages:
  - `RUN --mount=type=cache,id=nuget-<service>,target=/root/.nuget/packages,sharing=locked dotnet restore …`
  - `RUN --mount=type=cache,id=nuget-<service>,target=/root/.nuget/packages,sharing=locked dotnet publish …`
- Worker runtime stage (Playwright installation):
  - `RUN --mount=type=cache,id=npm-cache,target=/root/.npm \
        … npm i playwright@${PLAYWRIGHT_VERSION} && npx playwright install --with-deps`
  - Note: Do NOT mount `/ms-playwright` as a BuildKit cache in the final image stage. Files written to cache mounts are not committed to the image layers, which will lead to missing browser binaries at runtime.

How to enable BuildKit
- Docker Desktop (recent versions) enables BuildKit by default. If needed, you can force-enable it:
  - Linux/macOS: `export DOCKER_BUILDKIT=1`
  - Windows PowerShell: `$env:DOCKER_BUILDKIT=1`
- When using Docker Buildx (recommended), BuildKit is always used. Example:
  - `docker buildx build -t myimage:dev -f hub/Dockerfile .`

Local development tips
- Rebuild faster by keeping cache ids stable (already done) and avoiding unnecessary context invalidations.
- If you change only app code, the NuGet/npm/Playwright layers will stay cached.
- To completely reset caches, you can run `docker builder prune` (or selectively prune via `--filter type=cache`).

CI considerations
- These cache mounts work out-of-the-box on self-hosted runners and local machines.
- On ephemeral CI, pair with a remote cache/export if desired (e.g., `buildx build --cache-to/--cache-from`); this repo’s GitHub Actions already caches restore/npm layers separately.

Rationale and alignment
- Matches the improvement plan to reduce build times and network usage by leveraging persistent caches and better layer ordering.
- Keeps Dockerfiles simple and portable; no changes to application code paths.

Troubleshooting
- Build complains about unknown flag `--mount`: ensure BuildKit is on (see above) and the `# syntax=docker/dockerfile:1.4` header is present.
- If Playwright browsers appear to re-download frequently, confirm that `PLAYWRIGHT_BROWSERS_PATH=/ms-playwright` is set and the cache id remains constant.


Parallel builds and cache contention
- When building multiple services in parallel (e.g., `docker compose build`), using the same cache id for NuGet across images can cause contention and intermittent restore failures, for example:
  - `Could not find file '/root/.nuget/packages/microsoft.build.tasks.git/8.0.0/xxxxxxxx.ddo'`
- To avoid this:
  - Use distinct cache ids per service (e.g., `nuget-hub`, `nuget-dashboard`, `nuget-worker`).
  - Add `sharing=locked` to the cache mount to serialize writes to the cache when builds overlap.
  - Optionally add `sharing=locked` to npm and Playwright cache mounts if building many workers in parallel.
