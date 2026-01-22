# syntax=docker/dockerfile:1.4
# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy source
COPY . .
# Restore and publish using robust path detection to support different build contexts
WORKDIR /src
RUN --mount=type=cache,id=nuget-worker,target=/root/.nuget/packages,sharing=locked if [ -f "WorkerService.csproj" ]; then dotnet restore WorkerService.csproj; else dotnet restore worker/WorkerService.csproj; fi
RUN --mount=type=cache,id=nuget-worker,target=/root/.nuget/packages,sharing=locked if [ -f "WorkerService.csproj" ]; then dotnet publish WorkerService.csproj -c Release -o /app/publish; else dotnet publish worker/WorkerService.csproj -c Release -o /app/publish; fi


# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# App/runtime configuration
ENV DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://0.0.0.0:5000 \
    NODE_ENV=production \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    RES_OPTIONS="single-request timeout:1 attempts:1 ndots:1"

WORKDIR /app

# ✅ Add Node.js 20 without apt (much smaller & no index downloads)
COPY --from=node:20-bookworm-slim /usr/local /usr/local
RUN node -v && npm -v

# ✅ Install just curl for the healthcheck (small)
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl; \
    rm -rf /var/lib/apt/lists/*

# --- Install Playwright (JS package + browsers + system deps) ---
# Do this BEFORE copying the app so it caches unless the version changes
ARG PLAYWRIGHT_VERSION=1.54.2
ENV PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION}
RUN --mount=type=cache,id=npm-cache,target=/root/.npm \
    set -eux; \
    npm init -y; \
    npm i --no-save "playwright@${PLAYWRIGHT_VERSION}"; \
    npx playwright install --with-deps chromium chromium-headless-shell firefox webkit

# Bring in published app
COPY --from=build /app/publish .

EXPOSE 5000
HEALTHCHECK --interval=10s --timeout=10s --retries=5 --start-period=60s \
  CMD curl -fsSL http://127.0.0.1:5000/health || exit 1

ENTRYPOINT ["dotnet","WorkerService.dll"]
