# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first for better layer caching
COPY WorkerService.csproj ./
RUN dotnet restore

# Bring in the rest and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish


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
RUN set -eux; \
    npm init -y; \
    npm i --no-save "playwright@${PLAYWRIGHT_VERSION}"; \
    npx playwright install --with-deps

# Bring in published app
COPY --from=build /app/publish .

EXPOSE 5000
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=20s \
  CMD curl -fsSL http://127.0.0.1:5000/health || exit 1

ENTRYPOINT ["dotnet","WorkerService.dll"]